using System.Text;

namespace FhirMarshal.Models;

public class AuthConfig
{
    public string TokenEndpoint { get; set; } = string.Empty;

    public string CertPath { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string GrantType { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;

    public string AssertionType { get; set; } = string.Empty;

    public string ClientAssertionType { get; set; } = string.Empty;

    public string GetClientSecret()
    {
        var clientSecret = $"{ClientId}:{ClientSecret}";
        var clientSecretBytes = Encoding.UTF8.GetBytes(clientSecret);
        return Convert.ToBase64String(clientSecretBytes);
    }

    public bool IsAuthConfigValid()
    {
        if (AssertionType == "jwt")
            return !string.IsNullOrWhiteSpace(TokenEndpoint)
                && !string.IsNullOrWhiteSpace(ClientId)
                && !string.IsNullOrWhiteSpace(GrantType)
                && !string.IsNullOrWhiteSpace(CertPath);
        if (AssertionType == "client_secret")
            return !string.IsNullOrWhiteSpace(TokenEndpoint)
                && !string.IsNullOrWhiteSpace(ClientId)
                && !string.IsNullOrWhiteSpace(ClientSecret)
                && !string.IsNullOrWhiteSpace(GrantType);
        return false;
    }
}
