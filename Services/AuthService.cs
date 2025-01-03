using System.Text.Json;
using System.Text.Json.Serialization;

namespace FhirMarshal.Services;

public class AuthService
{
    private readonly IAuthConfigService _authConfigService;
    private readonly HttpClient _httpClient;
    private readonly JwtTokenService _jwtTokenService;
    private string? _accessToken;

    public AuthService(
        HttpClient httpClient,
        JwtTokenService jwtTokenService,
        IAuthConfigService authConfigService
    )
    {
        _httpClient = httpClient;
        _jwtTokenService = jwtTokenService;
        _authConfigService = authConfigService;
    }

    // Auth config


    public async Task<string> GetTokenAsync()
    {
        var authConfig = _authConfigService.GetAuthConfig();
        if (authConfig.AssertionType is "jwt")
            return _jwtTokenService.GenerateToken(authConfig.ClientId, authConfig.Audience);
        return string.Empty;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_accessToken))
            return _accessToken;

        var authConfig = _authConfigService.GetAuthConfig();

        if (authConfig.AssertionType is not "jwt")
            throw new InvalidOperationException(
                "Only JWT assertion type is supported at this time."
            );
        // if (!_authConfigService.IsAuthConfigValid())
        //     await _authConfigService.DisplayAuthConfigAsync();

        var token = await GetTokenAsync();
        var tokenRequest = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_assertion_type", authConfig.ClientAssertionType },
            { "client_assertion", token },
        };

        var response = await _httpClient.PostAsync(
            authConfig.TokenEndpoint,
            new FormUrlEncodedContent(tokenRequest),
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Failed to fetch OAuth token: {response.StatusCode} {error}"
            );
        }

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);

        if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("Failed to fetch OAuth token: Invalid response");

        _accessToken = tokenResponse.AccessToken;
        return _accessToken;
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}
