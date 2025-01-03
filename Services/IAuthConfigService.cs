using System.Text.Json;
using System.Text.Json.Nodes;
using FhirMarshal.Models;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace FhirMarshal.Services;

public interface IAuthConfigService
{
    AuthConfig GetAuthConfig();
    Task DisplayAuthConfigAsync();
    void SaveAuthConfiguration(AuthConfig authConfig);
    bool IsAuthConfigValid();
}

public class AuthConfigService : IAuthConfigService
{
    private readonly string _configPath;
    private readonly IConfiguration _configuration;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IServiceProvider _serviceProvider;
    private AuthConfig _authConfig;

    public AuthConfigService(
        IConfiguration configuration,
        IOptionsMonitor<AuthConfig> authConfig,
        IServiceProvider serviceProvider,
        JwtTokenService jwtTokenService
    )
    {
        _configuration = configuration;
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        _authConfig = authConfig.CurrentValue;
        _serviceProvider = serviceProvider;
        _jwtTokenService = jwtTokenService;
    }

    public AuthConfig GetAuthConfig()
    {
        if (_authConfig == null)
        {
            if (!File.Exists(_configPath))
            {
                _authConfig = new AuthConfig();
                SaveAuthConfiguration(_authConfig);
            }
            else
            {
                try
                {
                    var authConfigSection = _configuration.GetSection("AuthConfig");
                    if (authConfigSection.Exists())
                        _authConfig = authConfigSection.Get<AuthConfig>();
                    else
                        throw new JsonException("AuthConfig section is missing or malformed.");
                }
                catch
                {
                    Console.WriteLine("AuthConfig is invalid. Using default values.");
                    _authConfig = new AuthConfig();
                    SaveAuthConfiguration(_authConfig);
                }
            }
        }

        return _authConfig;
    }

    public void SaveAuthConfiguration(AuthConfig authConfig)
    {
        var configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        var json = File.ReadAllText(configFile);

        var jsonDocument = JsonDocument.Parse(json);
        var root = jsonDocument.RootElement.Clone();

        var authConfigJson = JsonSerializer.Serialize(authConfig);
        var configurationRoot = JsonSerializer.Deserialize<JsonObject>(root.GetRawText());

        if (configurationRoot != null)
        {
            configurationRoot["AuthConfig"] = JsonSerializer.Deserialize<JsonObject>(
                authConfigJson
            );

            var updatedConfigJson = JsonSerializer.Serialize(
                configurationRoot,
                new JsonSerializerOptions { WriteIndented = true }
            );

            File.WriteAllText(configFile, updatedConfigJson);
        }
        else
        {
            throw new InvalidOperationException("Configuration root is null");
        }
        _jwtTokenService.UpdateRsaSecurityKey();
    }

    public bool IsAuthConfigValid()
    {
        return _authConfig.IsAuthConfigValid();
    }

    public async Task DisplayAuthConfigAsync()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold blue]Setting[/]")
            .AddColumn("[bold blue]Value[/]");

        table.AddRow("TokenEndpoint", _authConfig.TokenEndpoint ?? string.Empty);
        table.AddRow("ClientId", _authConfig.ClientId ?? string.Empty);
        ;
        table.AddRow("CertPath", _authConfig.CertPath ?? string.Empty);
        table.AddRow("ClientSecret", "********");
        table.AddRow("Scope", _authConfig.Scope ?? string.Empty);
        table.AddRow("Audience", _authConfig.Audience ?? string.Empty);
        table.AddRow("GrantType", _authConfig.GrantType ?? string.Empty);
        table.AddRow("AssertionType", _authConfig.AssertionType ?? string.Empty);

        AnsiConsole.Write(table);
        var missingFields = GetMissingFields();
        if (!string.IsNullOrWhiteSpace(missingFields))
        {
            AnsiConsole.MarkupLine($"[bold red]Error: Missing required fields: {missingFields}[/]");
            EditAuthConfig(missingFields);
        }
        await Task.CompletedTask;
    }

    private string GetMissingFields()
    {
        var missingFields = string.Empty;

        if (string.IsNullOrWhiteSpace(_authConfig.AssertionType))
            missingFields += "AssertionType, ";
        if (string.IsNullOrWhiteSpace(_authConfig.TokenEndpoint))
            missingFields += "TokenEndpoint, ";
        if (string.IsNullOrWhiteSpace(_authConfig.ClientId))
            missingFields += "ClientId, ";
        if (string.IsNullOrWhiteSpace(_authConfig.Audience))
            missingFields += "Audience, ";
        if (string.IsNullOrWhiteSpace(_authConfig.GrantType))
            missingFields += "GrantType, ";
        if (_authConfig.AssertionType == "jwt" && string.IsNullOrWhiteSpace(_authConfig.CertPath))
            missingFields += "CertPath, ";
        if (_authConfig.AssertionType == "client_secret")
        {
            if (string.IsNullOrWhiteSpace(_authConfig.ClientSecret))
                missingFields += "ClientSecret, ";
            if (string.IsNullOrWhiteSpace(_authConfig.Scope))
                missingFields += "Scope, ";
        }

        return missingFields.TrimEnd(',', ' ');
    }

    private void EditAuthConfig(string missingFields)
    {
        var selectedProperties = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select a property to edit")
                .InstructionsText("Use Space to select, Enter to confirm")
                .PageSize(10)
                .AddChoices(missingFields.Split(", "))
        );

        foreach (var property in selectedProperties)
            UpdateAuthConfigurationItem(property);

        if (
            !string.IsNullOrWhiteSpace(_authConfig.CertPath)
            && string.IsNullOrWhiteSpace(_authConfig.AssertionType)
        )
            _authConfig.AssertionType = "jwt";

        SaveAuthConfiguration(_authConfig);
    }

    private void UpdateAuthConfigurationItem(string property)
    {
        switch (property)
        {
            case "TokenEndpoint":
                _authConfig.TokenEndpoint = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the TokenEndpoint")
                );
                break;
            case "CertPath":
                _authConfig.CertPath = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the CertPath")
                );
                break;
            case "ClientId":
                _authConfig.ClientId = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the ClientId")
                );
                break;
            case "ClientSecret":
                _authConfig.ClientSecret = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the Client Secret").Secret()
                );
                break;
            case "Scope":
                _authConfig.Scope = AnsiConsole.Prompt(new TextPrompt<string>("Enter the Scope"));
                break;
            case "Audience":
                _authConfig.Audience = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the Audience")
                );
                break;
            case "GrantType":
                _authConfig.GrantType = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the GrantType")
                );
                break;
            case "AssertionType":
                _authConfig.AssertionType = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the AssertionType")
                );
                break;
        }
    }
}
