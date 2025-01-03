using FhirMarshal.Config;
using FhirMarshal.Services;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FhirMarshal.Commands;

public class GetBulkDataCommand : Command<GetBulkDataCommandSettings>
{
    private readonly BulkDownloadService _bulkDownloadService;
    private readonly IFhirMarshalConfigService _configService;
    private FhirMarshalConfig _config;

    public GetBulkDataCommand(
        IFhirMarshalConfigService configService,
        IOptionsMonitor<FhirMarshalConfig> config,
        BulkDownloadService bulkDownloadService
    )
    {
        _bulkDownloadService = bulkDownloadService;
        _configService = configService;
        _config = config.CurrentValue;
        config.OnChange(updatedConfig => _config = updatedConfig);
    }

    public override int Execute(CommandContext context, GetBulkDataCommandSettings settings)
    {
        var numDl = settings.NumDl > 0 ? settings.NumDl : _config.NumDl;
        var urls = settings.Url ?? string.Empty;
        var acceptHeader = settings.AcceptHeader ?? _config.AcceptHeader;
        var output = settings.Output ?? _config.Output;
        // Check for all required fields: host, port, username, password, database, fhirVersion, input
        if (string.IsNullOrWhiteSpace(urls))
        {
            AnsiConsole.MarkupLine("[bold red]Error: No Url was provided.[/]");
            return 1;
        }

        if (settings.NumDl > 0)
            _config.NumDl = settings.NumDl;

        var missingFields = string.Empty;
        if (string.IsNullOrWhiteSpace(urls))
            missingFields += "url, ";
        if (numDl <= 0)
            missingFields += "numDl, ";
        if (string.IsNullOrWhiteSpace(acceptHeader))
            missingFields += "acceptHeader, ";
        if (string.IsNullOrWhiteSpace(output))
            _config.Output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
        if (!string.IsNullOrWhiteSpace(missingFields))
        {
            missingFields = missingFields.Substring(0, missingFields.Length - 2);
            AnsiConsole.MarkupLine($"[bold red]Error: Missing required fields: {missingFields}[/]");
            return 1;
        }

        var cleanUrls = ParseInput(urls);
        if (cleanUrls == null)
            return 1;
        foreach (var url in cleanUrls)
            try
            {
                _bulkDownloadService.DownloadBulkDataAsync(url).GetAwaiter().GetResult();
                AnsiConsole.MarkupLine("[bold green]Bulk download complete![/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error: {ex.Message}[/]");
                return -1;
            }

        return 0;
    }

    private List<string>? ParseInput(string input)
    {
        List<string>? urls = new();
        var splitInput = input.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var url in splitInput)
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                AnsiConsole.MarkupLine($"[bold red]Error: {url} is not a valid URL.[/]");
                return null;
            }
            else
            {
                urls.Add(url);
            }

        return urls;
    }
}
