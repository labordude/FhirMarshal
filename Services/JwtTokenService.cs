using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FhirMarshal.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FhirMarshal.Services;

public class JwtTokenService
{
    private readonly IOptionsMonitor<AuthConfig> _authConfigMonitor;
    private AuthConfig _currentConfig;
    private RsaSecurityKey _rsaSecurityKey;

    public JwtTokenService(IOptionsMonitor<AuthConfig> authConfigMonitor)
    {
        _authConfigMonitor = authConfigMonitor;
        _currentConfig = authConfigMonitor.CurrentValue;

        UpdateRsaSecurityKey();
        _authConfigMonitor.OnChange(OnConfigChanged);
    }

    private void OnConfigChanged(AuthConfig newConfig)
    {
        _currentConfig = newConfig;
        Console.WriteLine("AuthConfig has changed.");

        UpdateRsaSecurityKey();
    }

    public void UpdateRsaSecurityKey()
    {
        var certPath = _currentConfig.CertPath;

        if (string.IsNullOrWhiteSpace(certPath))
        {
            // Console.WriteLine("CertPath is not set.");
            return;
        }

        try
        {
            var cryptoProvider = new RSACryptoServiceProvider();
            cryptoProvider.ImportFromPem(
                File.ReadAllText(Path.Combine(certPath, "privatekey.pem"))
            );
            _rsaSecurityKey = new RsaSecurityKey(cryptoProvider);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            _rsaSecurityKey = null;
        }
    }

    public string GenerateToken(string clientId, string audience, int expiryMinutes = 5)
    {
        var authConfig = _authConfigMonitor.CurrentValue;
        if (authConfig.IsAuthConfigValid() && _rsaSecurityKey != null)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(
                    new[]
                    {
                        new Claim("sub", clientId),
                        new Claim("jti", Guid.NewGuid().ToString()),
                    }
                ),
                Issuer = clientId,
                Audience = audience,
                Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
                SigningCredentials = new SigningCredentials(
                    _rsaSecurityKey,
                    SecurityAlgorithms.RsaSha384Signature
                ),
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        Console.WriteLine("AuthConfig is invalid or RSA key is not set.");
        return string.Empty;
    }
}
