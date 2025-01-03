// See https://aka.ms/new-console-template for more information

using System.Net.Http.Headers;
using System.Text.Json;
using FhirMarshal.Commands;
using FhirMarshal.Config;
using FhirMarshal.Infrastructure;
using FhirMarshal.Models;
using FhirMarshal.Services;
using MudBlazor.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FhirMarshal;

public class Program
{
    public static async Task Main(string[] args)
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        var runtimeConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "runtimeSettings.json"
        );

        ValidateAndFixAppSettings(configPath);

        if (!File.Exists(runtimeConfigPath) && File.Exists(configPath))
            File.Copy(configPath, runtimeConfigPath);

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(
                (context, config) =>
                {
                    config
                        .AddJsonFile("appsettings.json", false, true)
                        .AddJsonFile("runtimeSettings.json", true, true)
                        .AddEnvironmentVariables()
                        .AddCommandLine(args);
                }
            )
            .ConfigureServices(
                (context, services) =>
                {
                    var configSection = context
                        .Configuration.GetSection("FhirMarshalConfig")
                        .Get<FhirMarshalConfig>();

                    services.AddMudServices();

                    services.Configure<FhirMarshalConfig>(
                        context.Configuration.GetSection("FhirMarshalConfig")
                    );
                    services.AddSingleton<IFhirMarshalConfigService, FhirMarshalConfigService>();
                    services.Configure<AuthConfig>(context.Configuration.GetSection("AuthConfig"));
                    services.AddSingleton<IAuthConfigService, AuthConfigService>();

                    services.AddSingleton<AuthService>();
                    services.AddScoped<JwtTokenService>();

                    services
                        .AddHttpClient("AuthenticatedClient")
                        .ConfigureHttpClient(
                            async (serviceProvider, client) =>
                            {
                                var authService = serviceProvider.GetRequiredService<AuthService>();
                                var token = await authService.GetTokenAsync();
                                client.DefaultRequestHeaders.Authorization =
                                    new AuthenticationHeaderValue("Bearer", token);
                            }
                        );
                    services.AddSingleton<BulkDownloadService>();

                    services.AddTransient<InitDbCommand>();
                    services.AddTransient<GetBulkDataCommand>();
                    services.AddTransient<LoadDbCommand>();
                    services.AddSingleton<WebHostCommand>();
                    services.AddSingleton<ICommandApp>(serviceProvider =>
                    {
                        var app = new CommandApp(new TypeRegistrar(services));
                        app.Configure(config =>
                        {
                            config.SetApplicationName("fhirmarshal");
                            config.ValidateExamples();

                            config
                                .AddCommand<InitDbCommand>("init")
                                .WithDescription("Initialize the database")
                                .WithExample("init");

                            config
                                .AddCommand<LoadDbCommand>("load")
                                .WithDescription("Load data into the database")
                                .WithExample("load");

                            config
                                .AddCommand<GetBulkDataCommand>("bulk")
                                .WithDescription("Get bulk data")
                                .WithExample("bulk");

                            config
                                .AddCommand<WebHostCommand>("web")
                                .WithDescription("Start the web server")
                                .WithExample("web");
                        });

                        return app;
                    });
                }
            )
            .Build();

        var config = host.Services.GetRequiredService<IFhirMarshalConfigService>();
        var commandApp = host.Services.GetRequiredService<ICommandApp>();

        if (args.Length == 0)
            await ShowMenu(config, commandApp);

        try
        {
            await commandApp.RunAsync(args);
        }
        catch (CommandParseException e)
        {
            AnsiConsole.MarkupLine($"[red]Error: {e.Message}[/]");
            throw new Exception(e.Message);
        }
    }

    private static async Task ShowMenu(
        IFhirMarshalConfigService configService,
        ICommandApp commandApp
    )
    {
        // Create the layout
        var supportsAnsi = AnsiConsole.Profile.Capabilities.Ansi;
        if (!supportsAnsi)
        {
            AnsiConsole.MarkupLine(
                "[red]This terminal does not support ANSI escape codes. Please enable ANSI escape codes to use this application[/]"
            );
            Environment.Exit(1);
        }

        // Render the layout
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(
                new Panel(new FigletText("FhirMarshal").Color(Color.Red).LeftJustified())
                    .Border(BoxBorder.None)
                    .Collapse()
            );
            var menuOptions = new List<string>
            {
                "Check configuration",
                "Initialize database",
                "Load data",
                "Download bulk data",
                "Start web server",
                "Exit",
            };

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>().HighlightStyle(Color.Red).AddChoices(menuOptions)
            );

            switch (selection)
            {
                case "Initialize database":
                    if (!configService.IsFhirMarshalConfigValid())
                    {
                        AnsiConsole.MarkupLine(
                            "[red]Configuration is invalid. Please update the settings[/]"
                        );
                        await configService.DisplayFhirMarshalConfigAsync();
                    }

                    AnsiConsole.MarkupLine("[green]Configuration is valid[/]");
                    AnsiConsole.MarkupLine("[green]Initializing database[/]");
                    var initResult = await commandApp.RunAsync(new[] { "init" });
                    if (initResult != 0)
                        AnsiConsole.MarkupLine("[red]Error initializing database[/]");

                    AnsiConsole.MarkupLine("[green]Press any key to continue...[/]");
                    Console.ReadKey();
                    break;
                case "Load data":
                    if (!configService.IsFhirMarshalConfigValid())
                    {
                        AnsiConsole.MarkupLine(
                            "[red]Configuration is invalid. Please update the settings[/]"
                        );
                        await configService.DisplayFhirMarshalConfigAsync();
                    }

                    var inputFile = AnsiConsole.Prompt(
                        new TextPrompt<string>("Enter the input file/directory")
                    );
                    AnsiConsole.MarkupLine($"[green]Loading data from {inputFile}...[/]");
                    var loadResult = await commandApp.RunAsync(
                        new[] { "load", "--input", inputFile }
                    );
                    if (loadResult != 0)
                        AnsiConsole.MarkupLine("[red]Error loading data[/]");
                    else
                        AnsiConsole.MarkupLine("[green]Data loaded successfully[/]");
                    AnsiConsole.MarkupLine("[green]Press any key to continue...[/]");
                    Console.ReadKey();
                    break;
                case "Download bulk data":
                    var inputUrl = AnsiConsole.Prompt(
                        new TextPrompt<string>("Enter the input file/url")
                    );
                    AnsiConsole.MarkupLine($"[green]Reaching out to {inputUrl}...[/]");
                    var bulkDownloadResult = await commandApp.RunAsync(
                        new[] { "bulk", "--input", inputUrl }
                    );
                    if (bulkDownloadResult != 0)
                        AnsiConsole.MarkupLine("[red]Error downloading bulk data[/]");
                    else
                        AnsiConsole.MarkupLine("[green]Bulk data downloaded successfully[/]");
                    AnsiConsole.MarkupLine("[green]Press any key to continue...[/]");
                    Console.ReadKey();
                    break;
                case "Start web server":
                    await commandApp.RunAsync(new[] { "web" });
                    Console.ReadKey();
                    break;
                case "Check configuration":
                    if (!configService.IsFhirMarshalConfigValid())
                    {
                        AnsiConsole.MarkupLine(
                            "[red]Configuration is invalid. Please update the settings[/]"
                        );
                        await configService.DisplayFhirMarshalConfigAsync();
                        break;
                    }

                    await configService.DisplayFhirMarshalConfigAsync();

                    break;
                case "Exit":
                    AnsiConsole.MarkupLine("[red]Exiting...[/]");
                    Environment.Exit(0);
                    break;
                default:
                    AnsiConsole.MarkupLine("[red]Invalid choice[/]");
                    break;
            }
        }
    }

    public static void ValidateAndFixAppSettings(string configPath)
    {
        var defaultConfig = new
        {
            FhirMarshalConfig = new
            {
                Host = string.Empty,
                WebHost = "localhost",
                WebPort = 3000,
                Port = 5432,
                Database = "fhirbase",
                Username = "postgres",
                Password = string.Empty,
                SslMode = "Prefer",
                FhirVersion = "4.0.0",
                AcceptHeader = "application/fhir+json",
                Mode = "insert",
                NumDl = 5,
            },
            AuthConfig = new
            {
                TokenEndpoint = string.Empty,
                CertPath = string.Empty,
                ClientId = string.Empty,
                ClientSecret = string.Empty,
                Scope = string.Empty,
                Audience = string.Empty,
                GrantType = "client_credentials",
                AssertionType = "jwt",
                ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            },
        };

        if (!File.Exists(configPath))
        {
            WriteConfigToFile(configPath, defaultConfig);
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var parsed = JsonSerializer.Deserialize<JsonElement>(json); // Validate JSON structure

            if (
                !parsed.TryGetProperty("FhirMarshalConfig", out _)
                || !parsed.TryGetProperty("AuthConfig", out _)
            )
                throw new JsonException("Missing required configuration sections.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Invalid configuration file: {ex.Message}. Rewriting to defaults.");
            WriteConfigToFile(configPath, defaultConfig);
        }
    }

    public static void WriteConfigToFile(string configPath, object config)
    {
        var json = JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(configPath, json);
        Console.WriteLine($"Default configuration written to: {configPath}");
    }
}
