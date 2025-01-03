using FhirMarshal.Config;
using FhirMarshal.Helpers;
using FhirMarshal.Models;
using FhirMarshal.Services;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FhirMarshal.Commands;

public class LoadDbCommand : Command<LoadDbCommandSettings>
{
    private readonly IFhirMarshalConfigService _configService;
    private FhirMarshalConfig _config;
    private IOptionsMonitor<FhirMarshalConfig> _optionsMonitor;

    public LoadDbCommand(
        IFhirMarshalConfigService configService,
        IOptionsMonitor<FhirMarshalConfig> optionsMonitor
    )
    {
        _configService = configService;
        _optionsMonitor = optionsMonitor;
        _config = configService.GetFhirMarshalConfig();
        optionsMonitor.OnChange(updatedConfig => _config = updatedConfig);
    }

    public override int Execute(CommandContext context, LoadDbCommandSettings settings)
    {
        var result = string.Empty;
        var host = settings.Host ?? _config.Host;
        var port = settings.Port > 0 ? settings.Port : _config.Port;
        var username = settings.Username ?? _config.Username;
        var password = settings.Password ?? _config.Password;
        var database = settings.Database ?? _config.Database;
        var mode = settings.Mode ?? "insert";
        var numDl = settings.NumDl > 0 ? settings.NumDl : 5;
        var fhirVersion = settings.FhirVersion ?? _config.FhirVersion;
        var input = settings.Input ?? new List<string>();

        // if a field is provided but not set in the config, set it in the config
        if (settings?.Host != null && settings.Host != _config.Host)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Host", settings.Host } }
            );
        if (settings?.Port > 0 && settings.Port != _config.Port)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Port", settings.Port.ToString() } }
            );
        if (settings?.Username != null && settings.Username != _config.Username)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Username", settings.Username } }
            );
        if (settings?.Password != null && settings.Password != _config.Password)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Password", settings.Password } }
            );
        if (settings?.Database != null && settings.Database != _config.Database)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Database", settings.Database } }
            );
        if (settings?.FhirVersion != null && settings.FhirVersion != _config.FhirVersion)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "FhirVersion", settings.FhirVersion } }
            );

        // Check for all required fields: host, port, username, password, database, fhirVersion, input
        // build a single string that contains all missing fields

        var missingFields = string.Empty;
        if (string.IsNullOrWhiteSpace(host))
            missingFields += "host, ";
        if (port <= 0)
            missingFields += "port, ";
        if (string.IsNullOrWhiteSpace(username))
            missingFields += "username, ";
        if (string.IsNullOrWhiteSpace(password))
            missingFields += "password, ";
        if (string.IsNullOrWhiteSpace(database))
            missingFields += "database, ";
        if (string.IsNullOrWhiteSpace(fhirVersion))
            missingFields += "fhirVersion, ";
        if (input.Count == 0)
            missingFields += "input file/url, ";

        if (!string.IsNullOrWhiteSpace(missingFields))
        {
            missingFields = missingFields.Substring(0, missingFields.Length - 2);
            AnsiConsole.MarkupLine($"[bold red]Error: Missing required fields: {missingFields}[/]");
            return 1;
        }

        if (mode != "insert" && mode != "copy")
        {
            AnsiConsole.MarkupLine(
                "[bold red]Error: Invalid mode. Mode must be either 'insert' or 'copy'[/]"
            );
            return 1;
        }

        var connString = _configService.GetConnectionString();
        Transformer.PreloadTransformData(_config.FhirVersion);
        var files = ParseInput(input);
        if (files == null)
            return 1;
        AnsiConsole.MarkupLine($"[bold green]Loading database: {database} on {host}:{port}[/]");
        if (mode == "copy")
        {
            // set numDl to 1
            numDl = 1;
            var copyResult = Task.Run(
                async () => await Loader.ExecuteLoad(connString, fhirVersion, files, mode)
            ).Result;
            AnsiConsole.MarkupLine(copyResult);
        }
        else
        {
            var insertResult = Task.Run(
                async () => await Loader.ExecuteLoad(connString, fhirVersion, files, mode)
            ).Result;
            AnsiConsole.MarkupLine(insertResult);
        }

        return 0;
    }

    private List<string>? ParseInput(List<string> input)
    {
        List<string>? files = new();

        foreach (var file in input)
        {
            if (!Directory.Exists(file) && !File.Exists(file))
            {
                AnsiConsole.MarkupLine(
                    $"[bold red]Error: {file} is not a valid file or directory.[/]"
                );
                return null;
            }

            files.Add(file);
        }

        return files;
    }
}
