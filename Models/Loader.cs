using System.Data;
using System.Text;
using System.Text.Json;
using FhirMarshal.Helpers;
using Npgsql;
using Spectre.Console;

namespace FhirMarshal.Models;

public interface ILoader
{
    Task LoadAsync(
        CancellationToken cancellationToken,
        string connString,
        IBundle bundle,
        Action<string, TimeSpan> cb
    );
}

public class Loader
{
    public string FhirVersion { get; set; } = "4.0.0";

    public static async Task<string> ExecuteLoad(
        string connString,
        string fhirVersion,
        List<string> input,
        string mode
    )
    {
        var files = await PrewalkDirectories(input);
        await LoadFiles(connString, fhirVersion, files, mode);
        return "Done";
    }

    private static Task<List<string>> PrewalkDirectories(List<string> input)
    {
        var files = new List<string>();
        foreach (var item in input)
            if (Directory.Exists(item))
                files.AddRange(Directory.GetFiles(item));
            else
                files.Add(item);

        return Task.FromResult(files);
    }

    private static async Task LoadFiles(
        string connString,
        string fhirVersion,
        List<string> files,
        string mode
    )
    {
        var newBundle = MultiFileBundle.CreateNewMultiFileBundle(files);

        if (newBundle.Count() == 0)
        {
            Console.WriteLine("No files to load");
            return;
        }

        var totalCount = newBundle.TotalResourceCount();
        var resourceCounts = new Dictionary<string, int>();
        var insertedCounts = new Dictionary<string, int>();
        var curResource = 0; // Initialize the resource counter
        var currentIndex = 0;

        // use the LoadInsertAsync method to load the files


        var tasks = new List<Task>();
        foreach (var bundle in newBundle.Bundles)
            tasks.Add(
                Task.Run(async () =>
                {
                    while (true)
                    {
                        var bundleTotal = bundle.Count;
                        var resourceStartTime = DateTime.UtcNow;
                        var (resource, error) = bundle.Next();

                        if (error != null && error.Message != "No more items in the bundle")
                        {
                            Console.WriteLine($"Error in resource {curResource}: {error.Message}");
                            break;
                        }

                        if (resource.Count == 0)
                            break;

                        try
                        {
                            var resourceType = resource.ContainsKey("resourceType")
                                ? Bundle.GetResourceType(resource)
                                : null;
                            if (string.IsNullOrEmpty(resourceType))
                                throw new Exception(
                                    $"Cannot determine resourceType for resource {resource}"
                                );

                            try
                            {
                                var loader = LoaderFactory.GetLoader(mode);
                                await loader.LoadAsync(
                                    CancellationToken.None,
                                    connString,
                                    bundle,
                                    (curType, duration) =>
                                    {
                                        currentIndex += bundleTotal;
                                        insertedCounts[curType] = insertedCounts.ContainsKey(
                                            curType
                                        )
                                            ? insertedCounts[curType] + 1
                                            : 1;
                                    }
                                );
                            }
                            catch (Exception e)
                                when (e is ArgumentException or InvalidOperationException)
                            {
                                Console.WriteLine($"Error creating loader: {e.Message}");
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Error loading resource: {e.Message}");
                            }

                            curResource += bundleTotal;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error in resource {curResource}: {e.Message}");
                        }
                    }

                    bundle.Close();
                })
            );
        await Task.WhenAll(tasks);
        Console.WriteLine("Finished loading resources");
    }
}

public static class LoaderFactory
{
    public static ILoader GetLoader(string mode)
    {
        return mode.ToLower() switch
        {
            "copy" => new CopyLoader(),
            "insert" => new InsertLoader(),
            _ => throw new ArgumentException($"Invalid mode: {mode}"),
        };
    }
}

public class InsertLoader : ILoader
{
    public string FhirVersion { get; set; } = "4.0.0";

    public async Task LoadAsync(
        CancellationToken cancellationToken,
        string connString,
        IBundle bundle,
        Action<string, TimeSpan> cb
    )
    {
        var logFilePath = "output.log";
        // check file exists and delete if it does then create a new one
        if (File.Exists(logFilePath))
            File.Delete(logFilePath);
        File.Create(logFilePath).Dispose();

        using var logFileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write);
        using var writer = new StreamWriter(logFileStream);
        var overallStartTime = DateTime.UtcNow;
        var totalCount = bundle.Count;
        var batchSize = 1000;
        var curResource = 0;
        var insertedCounts = new Dictionary<string, uint>();
        var batch = new NpgsqlBatch();
        // var tableName = string.Empty;
        await using var dataSource = NpgsqlDataSource.Create(connString);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        writer.WriteLine($"Database connection opened at: {DateTime.UtcNow}");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startTime = DateTime.UtcNow;

            var resource = bundle.Next();
            if (resource.Error != null)
                break;

            var transformStartTime = DateTime.UtcNow;
            var transformedResource = await Transformer.DoTransformAsync(
                resource.Item,
                FhirVersion
            );
            var transformDuration = DateTime.UtcNow - transformStartTime;
            writer.WriteLine($"Transform duration: {transformDuration}");

            if (transformedResource == null)
                throw new Exception("Cannot transform resource");

            var serializeStartTime = DateTime.UtcNow;
            var resourceJson = JsonSerializer.Serialize(transformedResource);
            if (resourceJson == null)
                throw new Exception("Cannot serialize resource");

            var serializeDuration = DateTime.UtcNow - serializeStartTime;
            writer.WriteLine($"Serialization duration: {serializeDuration}");

            var resourceType = Bundle.GetResourceType(resource.Item);
            if (resourceType == null)
                throw new Exception($"Cannot determine resourceType for resource {resource.Item}");

            var tableName = resourceType.ToLower();
            var resourceId = Bundle.GetResourceId(resource.Item);
            if (resourceId == null)
                throw new Exception($"Cannot determine resourceId for resource {resource.Item}");

            var query =
                resourceId == string.Empty
                    ? $"INSERT INTO {tableName} (id, txid, status, resource) VALUES(gen_random_uuid()::text, 0, 'created', @resource::jsonb)"
                    : $"INSERT INTO {tableName} (id, txid, status, resource) VALUES (@id::text, 0, 'created', @resource::jsonb)";

            var command = new NpgsqlBatchCommand(query);
            command.Parameters.AddWithValue("resource", resourceJson);
            if (!string.IsNullOrEmpty(resourceId))
                command.Parameters.AddWithValue("id", resourceId);

            batch.BatchCommands.Add(command);

            if (curResource % batchSize == 0 || curResource == totalCount - 1)
            {
                var batchStartTime = DateTime.UtcNow;
                writer.WriteLine($"Batch execution started at: {batchStartTime}");

                try
                {
                    batch.Connection = connection;
                    await batch.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing batch: {ex.Message}");
                }

                var batchDuration = DateTime.UtcNow - batchStartTime;
                writer.WriteLine($"Batch execution duration: {batchDuration}");

                cb?.Invoke(resourceType, DateTime.UtcNow - batchStartTime);

                batch.BatchCommands.Clear();
            }

            curResource++;
        }

        // Execute any remaining resources in the batch
        if (batch.BatchCommands.Count > 0)
            try
            {
                batch.Connection = connection;
                await batch.ExecuteNonQueryAsync();
                batch.BatchCommands.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing batch: {ex.Message}");
            }

        var overallDuration = DateTime.UtcNow - overallStartTime;
        writer.WriteLine($"Overall processing duration: {overallDuration}");
    }
}

public class CopyLoader : ILoader
{
    public string FhirVersion { get; set; } = "4.0.0";

    public async Task LoadAsync(
        CancellationToken cancellationToken,
        string connString,
        IBundle bundle,
        Action<string, TimeSpan> cb
    )
    {
        var logFilePath = "output.log";
        // check file exists and delete if it does then create a new one
        if (File.Exists(logFilePath))
            File.Delete(logFilePath);
        File.Create(logFilePath).Dispose();

        using var logFileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write);
        using var writer = new StreamWriter(logFileStream);
        var overallStartTime = DateTime.UtcNow;
        var totalCount = bundle.Count;
        var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var batchSize = Math.Min(10000, (int)(availableMemory / (1024 * 1024)) * 500);
        var curResource = 0;
        var insertedCounts = new Dictionary<string, uint>();
        var resourceBatch = new List<Dictionary<string, string>>();
        var tableName = string.Empty;
        await using var dataSource = NpgsqlDataSource.Create(connString);
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        writer.WriteLine($"Database connection opened at: {DateTime.UtcNow}");
        AnsiConsole.MarkupLine("[purple]using COPY method[/]");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startTime = DateTime.UtcNow;

            var resource = bundle.Next();
            if (resource.Error != null)
                break;

            var transformedResource = await Transformer.DoTransformAsync(
                resource.Item,
                FhirVersion
            );

            if (transformedResource == null)
                throw new Exception("Cannot transform resource");

            var resourceJson = JsonSerializer.Serialize(transformedResource);
            if (resourceJson == null)
                throw new Exception("Cannot serialize resource");

            var resourceType = Bundle.GetResourceType(resource.Item);
            if (resourceType == null)
                throw new Exception($"Cannot determine resourceType for resource {resource.Item}");

            tableName = resourceType.ToLower();
            var resourceId = Bundle.GetResourceId(resource.Item);
            if (resourceId == null)
                throw new Exception($"Cannot determine resourceId for resource {resource.Item}");

            resourceBatch.Add(new Dictionary<string, string> { { resourceId, resourceJson } });

            if (curResource % batchSize == 0 || curResource == totalCount - 1)
            {
                var batchStartTime = DateTime.UtcNow;
                writer.WriteLine($"Batch execution started at: {batchStartTime}");

                try
                {
                    await ExecuteCopyAsync(connection, resourceBatch, tableName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing batch: {ex.Message}");
                }

                var batchDuration = DateTime.UtcNow - batchStartTime;
                writer.WriteLine($"Batch execution duration: {batchDuration}");

                cb?.Invoke(resourceType, DateTime.UtcNow - batchStartTime);

                resourceBatch.Clear();
            }

            curResource++;
        }

        // Execute any remaining resources in the batch
        if (resourceBatch.Count > 0)
            try
            {
                await ExecuteCopyAsync(connection, resourceBatch, tableName);
                resourceBatch.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing batch: {ex.Message}");
            }

        var overallDuration = DateTime.UtcNow - overallStartTime;
        AnsiConsole.MarkupLineInterpolated($"Overall processing duration: {overallDuration}");
    }

    private static async Task ExecuteCopyAsync(
        NpgsqlConnection connection,
        List<Dictionary<string, string>> resourceBatch,
        string tableName
    )
    {
        var csvData = new StringBuilder();
        // make real sure the connection is open
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var resource in resourceBatch)
        {
            var id = resource.Keys.First();
            var resourceType = tableName.ToLower();
            var resourceJson = resource.Values.First().Replace("\"", "\"\"");
            csvData.AppendLine($"\"{id}\",0,\"{resourceType}\",\"created\",\"{resourceJson}\"");
        }

        var sql =
            $@"COPY {
                tableName
            }
        (id, txid, resource_type, status, resource) FROM STDIN WITH(FORMAT CSV);
        ";

        using (var writer = connection.BeginTextImport(sql))
        {
            await writer.WriteAsync(csvData.ToString());
        }
    }
}
