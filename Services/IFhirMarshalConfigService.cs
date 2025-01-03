using System.Text.Json;
using System.Text.Json.Nodes;
using FhirMarshal.Config;
using FhirMarshal.Models;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace FhirMarshal.Services;

public class AppSettings
{
    public FhirMarshalConfig FhirMarshalConfig { get; set; }
    public AuthConfig AuthConfig { get; set; }
}

public interface IFhirMarshalConfigService
{
    FhirMarshalConfig GetFhirMarshalConfig();
    Task DisplayFhirMarshalConfigAsync();
    void SaveFhirMarshalConfiguration(FhirMarshalConfig fhirMarshalConfig);
    void UpdateFhirMarshalConfigurationItem(Dictionary<string, string> property);
    bool IsDbConfigValid();
    bool IsFhirMarshalConfigValid();
    string GetConnectionString();
}

public class FhirMarshalConfigService : IFhirMarshalConfigService
{
    private readonly string _configPath;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<FhirMarshalConfig> _optionsMonitor;
    private readonly string _runtimeSettingsPath;
    private FhirMarshalConfig _fhirMarshalConfig;

    public FhirMarshalConfigService(
        IConfiguration configuration,
        IOptionsMonitor<FhirMarshalConfig> optionsMonitor
    )
    {
        _configuration = configuration;
        _optionsMonitor = optionsMonitor;
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        _runtimeSettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "runtimeSettings.json"
        );
        _fhirMarshalConfig = LoadConfig();
        _optionsMonitor.OnChange(updatedConfig =>
        {
            _fhirMarshalConfig = updatedConfig;
        });
    }

    public FhirMarshalConfig GetFhirMarshalConfig()
    {
        return _fhirMarshalConfig ?? _optionsMonitor.CurrentValue;
    }

    public void SaveFhirMarshalConfiguration(FhirMarshalConfig fhirMarshalConfig)
    {
        UpdateRuntimeSettings(fhirMarshalConfig);
        // var configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        // var json = File.ReadAllText(configFile);
        //
        // var jsonDocument = JsonDocument.Parse(json);
        // var root = jsonDocument.RootElement.Clone();
        //
        // var fhirMarshalConfigJson = JsonSerializer.Serialize(fhirMarshalConfig);
        // var configurationRoot = JsonSerializer.Deserialize<JsonObject>(root.GetRawText());
        //
        // if (configurationRoot != null)
        // {
        //     configurationRoot["FhirMarshalConfig"] = JsonSerializer.Deserialize<JsonObject>(
        //         fhirMarshalConfigJson
        //     );
        //
        //     var updatedConfigJson = JsonSerializer.Serialize(
        //         configurationRoot,
        //         new JsonSerializerOptions { WriteIndented = true }
        //     );
        //
        //     File.WriteAllText(configFile, updatedConfigJson);
        //     _fhirMarshalConfig = fhirMarshalConfig;
        //
        //     _optionsMonitor.OnChange(updatedConfig =>
        //     {
        //         _fhirMarshalConfig = updatedConfig;
        //     });
        // }
        // else
        // {
        //     throw new InvalidOperationException("Failed to deserialize the configuration root.");
        // }
    }

    public bool IsDbConfigValid()
    {
        var fhirMarshalConfig = GetFhirMarshalConfig();
        return !string.IsNullOrWhiteSpace(fhirMarshalConfig.Host)
            && fhirMarshalConfig.Port != 0
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.Database)
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.Username)
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.Password)
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.SslMode);
    }

    public bool IsFhirMarshalConfigValid()
    {
        var fhirMarshalConfig = GetFhirMarshalConfig();
        return !string.IsNullOrWhiteSpace(fhirMarshalConfig.Host)
            && fhirMarshalConfig.Port != 0
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.Database)
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.Username)
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.Password)
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.SslMode)
            && !string.IsNullOrWhiteSpace(fhirMarshalConfig.FhirVersion);
    }

    public string GetConnectionString()
    {
        if (!IsDbConfigValid())
            throw new InvalidOperationException("Database configuration is not valid");
        return $"Host={_fhirMarshalConfig.Host};Port={_fhirMarshalConfig.Port};Database={_fhirMarshalConfig.Database};Username={_fhirMarshalConfig.Username};Password={_fhirMarshalConfig.Password};SslMode={_fhirMarshalConfig.SslMode};Pooling=true;MinPoolSize=10;MaxPoolSize=200;";
    }

    public async Task DisplayFhirMarshalConfigAsync()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold blue]Setting[/]")
            .AddColumn("[bold blue]Value[/]");

        table.AddRow("Postgres Host", _fhirMarshalConfig.Host ?? string.Empty);
        table.AddRow("Postgres Port", _fhirMarshalConfig.Port.ToString());
        table.AddRow("Postgres Database", _fhirMarshalConfig.Database ?? string.Empty);
        table.AddRow("Postgres Username", _fhirMarshalConfig.Username ?? string.Empty);
        table.AddRow(
            "Postgres Password",
            !string.IsNullOrWhiteSpace(_fhirMarshalConfig.Password) ? "********" : string.Empty
        );
        table.AddRow("Postgres SSL Mode", _fhirMarshalConfig.SslMode ?? string.Empty);
        table.AddRow("FHIR Version", _fhirMarshalConfig.FhirVersion ?? string.Empty);
        table.AddRow("Web Host", _fhirMarshalConfig.WebHost ?? string.Empty);
        table.AddRow("Web Port", _fhirMarshalConfig.WebPort.ToString());

        AnsiConsole.Write(table);
        var missingFields = GetMissingFields();
        if (!string.IsNullOrWhiteSpace(missingFields))
        {
            AnsiConsole.MarkupLine($"[bold red]Error: Missing required fields: {missingFields}[/]");
            EditFhirMarshalConfig(missingFields);
        }

        var editsNeeded = AnsiConsole.Prompt(
            new TextPrompt<bool>("[bold yellow]Do you need to edit anything?[/]")
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(false)
                .WithConverter(choice => choice ? "y" : "n")
        );

        if (editsNeeded)
        {
            var allFields =
                "Host, Port, Database, Username, Password, SslMode, FhirVersion, WebHost, WebPort";
            EditFhirMarshalConfig(allFields);
        }

        await Task.CompletedTask;
    }

    public void UpdateFhirMarshalConfigurationItem(Dictionary<string, string> property)
    {
        var fhirMarshalConfig = GetFhirMarshalConfig();
        foreach (var item in property)
            switch (item.Key)
            {
                case "Host":
                    fhirMarshalConfig.Host = item.Value;
                    break;
                case "Port":
                    fhirMarshalConfig.Port = int.TryParse(item.Value, out var port) ? port : 5432;
                    break;
                case "Database":
                    fhirMarshalConfig.Database = item.Value;
                    break;
                case "Username":
                    fhirMarshalConfig.Username = item.Value;
                    break;
                case "Password":
                    fhirMarshalConfig.Password = item.Value;
                    break;
                case "SslMode":
                    fhirMarshalConfig.SslMode = item.Value;
                    break;
                case "FhirVersion":
                    fhirMarshalConfig.FhirVersion = item.Value;
                    break;
                case "WebHost":
                    fhirMarshalConfig.WebHost = item.Value;
                    break;
                case "WebPort":
                    fhirMarshalConfig.WebPort = int.TryParse(item.Value, out var webPort)
                        ? webPort
                        : 3000;
                    break;
            }

        SaveFhirMarshalConfiguration(fhirMarshalConfig);
    }

    // public FhirMarshalConfig GetFhirMarshalConfig()
    // {
    //     // var configSection = _configuration.GetSection("FhirMarshalConfig");
    //     // return configSection.Exists()
    //     //     ? configSection.Get<FhirMarshalConfig>()
    //     //     : new FhirMarshalConfig();
    //     if (_fhirMarshalConfig == null)
    //     {
    //         if (!File.Exists(_configPath))
    //         {
    //             _fhirMarshalConfig = new FhirMarshalConfig();
    //             SaveFhirMarshalConfiguration(_fhirMarshalConfig);
    //         }
    //         else
    //         {
    //             try
    //             {
    //                 var fhirMarshalConfigSection = _configuration.GetSection("FhirMarshalConfig");
    //                 if (fhirMarshalConfigSection.Exists())
    //                     _fhirMarshalConfig =
    //                         fhirMarshalConfigSection.Get<FhirMarshalConfig>()
    //                         ?? new FhirMarshalConfig();
    //                 else
    //                     throw new JsonException(
    //                         "FhirMarshalConfig section is missing or malformed."
    //                     );
    //             }
    //             catch
    //             {
    //                 Console.WriteLine("FhirMarshalConfig is invalid. Using default values.");
    //                 _fhirMarshalConfig = new FhirMarshalConfig();
    //                 SaveFhirMarshalConfiguration(_fhirMarshalConfig);
    //             }
    //         }
    //     }
    //
    //     return _fhirMarshalConfig;
    // }
    public void UpdateRuntimeSettings(FhirMarshalConfig updatedConfig)
    {
        // Save to runtimeSettings.json (runtime-only changes)
        var json = JsonSerializer.Serialize(
            updatedConfig,
            new JsonSerializerOptions { WriteIndented = true }
        );

        var currentRuntimeSettings = File.ReadAllText(_runtimeSettingsPath);
        var currentSettings = JsonSerializer.Deserialize<AppSettings>(currentRuntimeSettings);
        if (currentSettings != null)
        {
            currentSettings.FhirMarshalConfig = updatedConfig;
            json = JsonSerializer.Serialize(
                currentSettings,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }

        File.WriteAllText(_runtimeSettingsPath, json);

        // Save to appsettings.json (persistent changes)
        SaveAppSettings(updatedConfig);

        _fhirMarshalConfig = updatedConfig;

        // Reload the configuration to reflect the changes
        _optionsMonitor.OnChange(options =>
        {
            _fhirMarshalConfig = updatedConfig;
        });
    }

    private void SaveAppSettings(FhirMarshalConfig updatedConfig)
    {
        // Load current appsettings.json
        var json = File.ReadAllText(_configPath);
        var jsonDocument = JsonDocument.Parse(json);
        var root = jsonDocument.RootElement.Clone();

        var updatedConfigJson = JsonSerializer.Serialize(updatedConfig);
        var configurationRoot = JsonSerializer.Deserialize<JsonObject>(root.GetRawText());

        if (configurationRoot != null)
        {
            configurationRoot["FhirMarshalConfig"] = JsonSerializer.Deserialize<JsonObject>(
                updatedConfigJson
            );

            // Write the updated content back to appsettings.json
            var updatedConfigJsonString = JsonSerializer.Serialize(
                configurationRoot,
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(_configPath, updatedConfigJsonString);
        }
        else
        {
            throw new InvalidOperationException("Failed to deserialize the configuration root.");
        }
    }

    private FhirMarshalConfig LoadConfig()
    {
        if (File.Exists(_runtimeSettingsPath))
            try
            {
                var jsonContent = File.ReadAllText(_runtimeSettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(jsonContent);

                if (settings != null && settings.FhirMarshalConfig != null)
                {
                    var currentConfig = settings.FhirMarshalConfig;
                    _optionsMonitor.OnChange(config => { });
                    return currentConfig;
                } // Return the FhirMarshalConfig section from appsettings.json

                // Console.WriteLine("FhirMarshalConfig is missing or invalid.");
                try
                {
                    var appSettings = _configuration.GetSection("FhirMarshalConfig");
                    if (appSettings.Exists())
                    {
                        var fhirMarshalConfig = appSettings.Get<FhirMarshalConfig>();
                        if (fhirMarshalConfig != null)
                        {
                            _optionsMonitor.OnChange(config => { });
                            return fhirMarshalConfig;
                        }
                    }
                }
                catch
                {
                    Console.WriteLine("FhirMarshalConfig is invalid. Using default values.");
                    return new FhirMarshalConfig();
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during deserialization: {ex.Message}");
                return new FhirMarshalConfig();
            }

        Console.WriteLine("runtimeSettings.json does not exist.");
        return new FhirMarshalConfig();
    }

    private string GetMissingFields()
    {
        var missingFields = string.Empty;

        if (string.IsNullOrWhiteSpace(_fhirMarshalConfig.Host))
            missingFields += "Host, ";
        if (_fhirMarshalConfig.Port == 0)
            missingFields += "Port, ";
        if (string.IsNullOrWhiteSpace(_fhirMarshalConfig.Database))
            missingFields += "Database, ";
        if (string.IsNullOrWhiteSpace(_fhirMarshalConfig.Username))
            missingFields += "Username, ";
        if (string.IsNullOrWhiteSpace(_fhirMarshalConfig.Password))
            missingFields += "Password, ";
        if (string.IsNullOrWhiteSpace(_fhirMarshalConfig.SslMode))
            missingFields += "SslMode, ";
        if (string.IsNullOrWhiteSpace(_fhirMarshalConfig.FhirVersion))
            missingFields += "FhirVersion, ";
        // if (string.IsNullOrWhiteSpace(_fhirMarshalConfig.WebHost))
        //     missingFields += "WebHost, ";
        // if (_fhirMarshalConfig.WebPort == 0)
        //     missingFields += "WebPort, ";

        return missingFields.TrimEnd(',', ' ');
    }

    private void EditFhirMarshalConfig(string missingFields)
    {
        var selectedProperties = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select a property to edit")
                .InstructionsText(
                    "Use Space to select then Enter to edit. If you're done, press Enter without selecting anything."
                )
                .PageSize(10)
                .AddChoices(missingFields.Split(", "))
                .NotRequired()
        );

        foreach (var property in selectedProperties)
            UpdateFhirMarshalConfigItem(property);

        SaveFhirMarshalConfiguration(_fhirMarshalConfig);
    }

    private void UpdateFhirMarshalConfigItem(string property)
    {
        switch (property)
        {
            case "Host":
                _fhirMarshalConfig.Host = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter Postgres Host")
                );
                break;
            case "Port":
                _fhirMarshalConfig.Port = AnsiConsole.Prompt(
                    new TextPrompt<int>("Enter Postgres Port").DefaultValue(5432)
                );
                break;
            case "Database":
                _fhirMarshalConfig.Database = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter Postgres Database").DefaultValue("fhirbase")
                );
                break;
            case "Username":
                _fhirMarshalConfig.Username = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter Postgres Username").DefaultValue("postgres")
                );
                break;
            case "Password":
                _fhirMarshalConfig.Password = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter Postgres Password").Secret()
                );
                break;

            case "SslMode":
                _fhirMarshalConfig.SslMode = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter Postgres SSL Mode").DefaultValue("Disable")
                );
                break;
            case "FhirVersion":
                _fhirMarshalConfig.FhirVersion = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter FHIR Version").DefaultValue("4.0.0")
                );
                break;
            case "WebHost":
                _fhirMarshalConfig.WebHost = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter Web Host").DefaultValue("localhost")
                );
                break;
            case "WebPort":
                _fhirMarshalConfig.WebPort = AnsiConsole.Prompt(
                    new TextPrompt<int>("Enter Web Port").DefaultValue(3000)
                );
                break;
        }
    }
}
