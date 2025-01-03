using System.Text.Json;
using FhirMarshal.Config;
using FhirMarshal.Services;
using Microsoft.Extensions.Options;
using Npgsql;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FhirMarshal.Commands;

public class InitDbCommand : Command<InitDbCommandSettings>
{
    private readonly IFhirMarshalConfigService _configService;
    private FhirMarshalConfig _config;
    private IOptionsMonitor<FhirMarshalConfig> _optionsMonitor;

    public InitDbCommand(
        IFhirMarshalConfigService configService,
        IOptionsMonitor<FhirMarshalConfig> optionsMonitor
    )
    {
        _configService = configService;
        _optionsMonitor = optionsMonitor;
        _config = configService.GetFhirMarshalConfig();
        optionsMonitor.OnChange(updatedConfig => _config = updatedConfig);
    }

    public override int Execute(CommandContext context, InitDbCommandSettings settings)
    {
        var host = settings.Host ?? _config.Host;
        var port = settings.Port > 0 ? settings.Port : _config.Port;
        var username = settings.Username ?? _config.Username;
        var password = settings.Password ?? _config.Password;
        var database = settings.Database ?? _config.Database;
        var fhirVersion = settings.FhirVersion ?? _config.FhirVersion;

        // if a field is provided but not set in the config, set it in the config
        if (settings.Host != null)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Host", settings.Host } }
            );
        if (settings.Port > 0)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Port", settings.Port.ToString() } }
            );
        if (settings.Username != null)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Username", settings.Username } }
            );
        if (settings.Password != null)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Password", settings.Password } }
            );
        if (settings.Database != null)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "Database", settings.Database } }
            );
        if (settings.FhirVersion != null)
            _configService.UpdateFhirMarshalConfigurationItem(
                new Dictionary<string, string> { { "FhirVersion", settings.FhirVersion } }
            );

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

        if (!string.IsNullOrWhiteSpace(missingFields))
        {
            missingFields = missingFields.Substring(0, missingFields.Length - 2);
            AnsiConsole.MarkupLine($"[bold red]Error: Missing required fields: {missingFields}[/]");
            return 1;
        }

        var connString = _configService.GetConnectionString();

        // run the initialize database function
        var result = InitalizeDatabase(connString, fhirVersion).Result;

        if (result == "Ok")
        {
            AnsiConsole.MarkupLine("[bold green]Database initialized successfully[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[bold red]Error initializing database[/]");
        return 1;
    }

    public static async Task<string> InitalizeDatabase(string connString, string fhirVersion)
    {
        List<string> conceptsTables =
        [
            """
                    CREATE TABLE IF NOT EXISTS "concept" (
                    
                            id text primary key,
                            txid bigint not null,
                            ts timestamptz DEFAULT current_timestamp,
                            resource_type text default 'Concept',
                            status resource_status not null,
                            resource jsonb not null);
                """,
            """
                    CREATE TABLE IF NOT EXISTS "concept_history" (
                        id text,
                        txid bigint not null,
                        ts timestamptz DEFAULT current_timestamp,
                        resource_type text default 'Concept',
                        status resource_status not null,
                        resource jsonb not null,
                        PRIMARY KEY (id, txid)
                    );
                """,
        ];

        var schemaFileName = $"Assets/schema/fhirbase-{fhirVersion}.sql.json";
        var functionFileName = "Assets/schema/functions.sql.json";
        // read the schema file into schemaStatements
        await using var json = File.OpenRead(schemaFileName);
        var schemaStatements = JsonSerializer.Deserialize<List<string>>(json);
        if (schemaStatements == null)
        {
            AnsiConsole.MarkupLine(
                $"[bold red]Error: Could not read schema file {schemaFileName}[/]"
            );
            return "Error";
        }

        AnsiConsole.MarkupLine(
            $"[bold green]Read {schemaStatements.Count} schema statements from {schemaFileName}[/]"
        );

        // read the function file into functionStatements

        await using var functionsJson = File.OpenRead(functionFileName);
        var functionStatements = JsonSerializer.Deserialize<List<string>>(functionsJson);

        if (functionStatements == null)
        {
            AnsiConsole.MarkupLine(
                $"[bold red]Error: Could not read function file {functionFileName}[/]"
            );
            return "Error";
        }

        AnsiConsole.MarkupLine(
            $"[bold green]Read {functionStatements.Count} function statements from {functionFileName}[/]"
        );

        var allStatements = schemaStatements
            .Concat(functionStatements)
            .Concat(conceptsTables)
            .ToList();
        try
        {
            await CheckDbExists(connString);
            await using var dataSource = NpgsqlDataSource.Create(connString);
            await ExecuteBatchAsync(dataSource, allStatements);
        }
        catch (NpgsqlException ex) when (ex.SqlState == "42P07")
        {
            AnsiConsole.MarkupLine("[bold yellow]Database already initialized[/]");
        }
        catch (NpgsqlException ex) when (ex.SqlState == "42P01")
        {
            AnsiConsole.MarkupLine("[bold yellow]Table already exists[/]");
        }
        catch (NpgsqlException ex) when (ex.SqlState == "3D000")
        {
            AnsiConsole.MarkupLine("[bold red]Database does not exist[/]");
        }
        catch (AggregateException ex)
        {
            foreach (var innerException in ex.InnerExceptions)
                Console.WriteLine(innerException.Message);
        }

        return "Ok";
    }

    private static async Task CheckDbExists(string connString)
    {
        var database = connString
            .Split(';')
            .First(x => x.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
            .Split('=')[1];
        var newConnString = new NpgsqlConnectionStringBuilder(connString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(newConnString);
        await connection.OpenAsync();

        var query = "SELECT 1 FROM pg_database WHERE datname = @database";
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("database", database);

        var result = await command.ExecuteScalarAsync();
        if (result == null)
        // create the database
        {
            query = $"CREATE DATABASE {database}";
            await using var createDbCommand = new NpgsqlCommand(query, connection);
            await createDbCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteBatchAsync(
        NpgsqlDataSource dataSource,
        List<string> statements
    )
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var batch = dataSource.CreateBatch();
        foreach (var statement in statements)
            batch.BatchCommands.Add(new NpgsqlBatchCommand(statement));

        await batch.ExecuteNonQueryAsync();
        AnsiConsole.MarkupLine("[bold green]Batch executed successfully[/]");
    }
}
