using System.Text.Json;
using FhirMarshal.Components;
using FhirMarshal.Config;
using FhirMarshal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Npgsql;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FhirMarshal.Commands;

public class WebHostCommand : Command<WebHostCommandSettings>
{
    private readonly IFhirMarshalConfigService _configService;
    private readonly IServiceProvider _serviceProvider;
    private FhirMarshalConfig _config;

    public WebHostCommand(
        IFhirMarshalConfigService configService,
        IOptionsMonitor<FhirMarshalConfig> config,
        IServiceProvider provider
    )
    {
        _configService = configService;
        _config = config.CurrentValue;

        config.OnChange(updatedConfig =>
        {
            _config = updatedConfig;
        });

        _serviceProvider = provider;
    }

    public override int Execute(CommandContext context, WebHostCommandSettings settings)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            // AnsiConsole.MarkupLine("[bold red]Shutdown initiated...[/]");
            cancellationTokenSource.Cancel();
            eventArgs.Cancel = true;
        };
        var host = settings.WebHost ?? _config.WebHost;
        var port = settings.WebPort ?? _config.WebPort;

        if (
            !string.IsNullOrWhiteSpace(settings.WebHost)
            && !settings.WebHost.Equals(_config.WebHost)
        )
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "WebHost", settings.WebHost } }
            );
        if (settings.WebPort > 0 && settings.WebPort != _config.WebPort)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "WebPort", settings.WebPort.ToString() } }
            );

        if (!_configService.IsFhirMarshalConfigValid())
        {
            AnsiConsole.MarkupLine("[bold red]Invalid configuration[/]");
            _configService.DisplayFhirMarshalConfigAsync();
        }

        var webServerTask = Task.Run(async () =>
        {
            await StartWebApiAsync(_configService, _serviceProvider, cancellationTokenSource.Token);
        });
        var hostString = $"http://{_config.WebHost}:{_config.WebPort}";
        Console.WriteLine("Web API server started at {0}", hostString);
        AnsiConsole.MarkupLine("[bold red]Press any key to stop the server...[/]");
        var keyPressTask = Task.Run(() =>
        {
            Console.ReadKey(true);
            cancellationTokenSource.Cancel();
        });

        try
        {
            Task.WhenAny(webServerTask, keyPressTask).GetAwaiter().GetResult();
            if (webServerTask.IsFaulted)
            {
                Console.WriteLine("Web API server faulted: {0}", webServerTask.Exception?.Message);
                if (webServerTask.Exception is not null)
                    foreach (var ex in webServerTask.Exception.InnerExceptions)
                        Console.WriteLine(ex.Message);
            }

            AnsiConsole.MarkupLine("[bold blue]Server stopping...[/]");
        }
        catch (Exception ex)
        {
            Console.WriteLine("An unexpected error has occurred: {0}", ex.Message);
            return 1;
        }
        AnsiConsole.MarkupLine("[bold green]Server stopped[/]");
        AnsiConsole.MarkupLine("[bold green]Press any key to continue![/]");
        return 0;
    }

    private static async Task StartWebApiAsync(
        IFhirMarshalConfigService configService,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default
    )
    {
        var config = configService.GetFhirMarshalConfig();
        var webHost = config.WebHost;
        var webPort = config.WebPort;

        if (string.IsNullOrWhiteSpace(webHost) || webPort == 0)
        {
            AnsiConsole.MarkupLine("[bold red]Invalid configuration[/]");
            await configService.DisplayFhirMarshalConfigAsync();
        }

        if (!webHost.StartsWith("http://") && !webHost.StartsWith("https://"))
            webHost = $"http://{webHost}";

        var hostString = $"{webHost}:{webPort}";
        var builder = WebApplication.CreateBuilder();

        // services galore
        builder.Services.AddMudServices();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(serviceProvider);

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(hostString) });
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            });
        });
        // Kestrel config run in 12 parsecs!
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AllowSynchronousIO = true;
        });

        builder.WebHost.UseUrls($"{hostString}");

        // logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();

        // middling middleware
        app.UseCors();
        // app.UseHttpsRedirection();
        app.UseRouting();
        app.MapStaticAssets();
        app.UseAntiforgery();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        // just a few more routes
        app.MapGet(
            "/api/query",
            async ([FromQuery(Name = "query")] string query) =>
            {
                var connString = configService.GetConnectionString();
                var results = await HandleQueryAsync(query, connString);
                return results;
            }
        );

        // let's go!
        try
        {
            AnsiConsole.MarkupLine("[bold green]Server starting...[/]");
            await app.RunAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[bold yellow]Server interrupted[/]{ex.Message}");
            throw;
        }
    }

    private static async Task<List<JsonElement>> HandleQueryAsync(string query, string connString)
    {
        var results = new List<JsonElement>();

        await using (var connection = new NpgsqlConnection(connString))
        {
            await connection.OpenAsync();
            using (var command = new NpgsqlCommand(query, connection))
            {
                try
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object>();
                            for (var i = 0; i < reader.FieldCount; i++)
                                row[reader.GetName(i)] = reader.GetValue(i);

                            var json = JsonSerializer.Serialize(row);
                            var jsonElement = JsonDocument.Parse(json).RootElement;
                            results.Add(jsonElement);
                        }
                    }
                }
                catch (PostgresException ex)
                {
                    AnsiConsole.MarkupLine($"[bold red]Error preparing query: {ex.Message}[/]");
                    // return an error message as a JSON object
                    results.Add(JsonDocument.Parse($"{{\"error\": \"{ex.Message}\"}}").RootElement);
                }
            }
        }

        // return the results as a JSON array...finally
        return results;
    }
}
