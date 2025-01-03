using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FhirMarshal.Config;
using Spectre.Console;

namespace FhirMarshal.Services;

public class BulkDownloadService
{
    private readonly IAuthConfigService _authConfigService;
    private readonly AuthService _authService;
    private readonly IFhirMarshalConfigService _configService;
    private readonly HttpClient _httpClient;
    private readonly FhirMarshalConfig _config;

    public BulkDownloadService(
        IHttpClientFactory httpClientFactory,
        AuthService authService,
        IFhirMarshalConfigService configService,
        IAuthConfigService authConfigService
    )
    {
        _httpClient = httpClientFactory.CreateClient("AuthenticatedClient");
        _authService = authService;
        _configService = configService;
        _authConfigService = authConfigService;
        _config = _configService.GetFhirMarshalConfig();
    }

    private static string ContentLocation { get; set; } = string.Empty;
    private static int WaitTime { get; set; } = 120;
    private static int MaxRetries { get; } = 5;
    private static List<FileListing> BulkFiles { get; } = new();

    public async Task DownloadBulkDataAsync(
        string url,
        CancellationToken cancellationToken = default
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var authNeeded = AnsiConsole.Prompt(
            new TextPrompt<bool>(
                "[bold yellow]Do you need to authenticate to download the data?[/]"
            )
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(false)
                .WithConverter(choice => choice ? "y" : "n")
        );
        if (authNeeded)
        {
            if (!_authConfigService.IsAuthConfigValid())
            {
                AnsiConsole.MarkupLine("[bold red]Error: Invalid authentication configuration.[/]");
                await _authConfigService.DisplayAuthConfigAsync();
            }

            var token = await _authService.GetAccessTokenAsync(cancellationToken);

            // set the auth header and the respond-async header
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        _httpClient.DefaultRequestHeaders.Add("Accept", _config.AcceptHeader);
        request.Headers.Add("Prefer", "respond-async");
        var output = _config.Output;
        if (!Directory.Exists(output))
            Directory.CreateDirectory(output);
        if (!url.Contains("$export-poll-status"))
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold red]Error: {response.StatusCode}[/]");
                response.WriteRequestToConsole();
                return;
            }

            // get the content location
            var responseHeaders = response.Content.Headers.ContentLocation;
            if (responseHeaders != null)
            {
                ContentLocation = responseHeaders.ToString();
                AnsiConsole.MarkupLine($"[bold green]Content-Location: {ContentLocation}[/]");
            }
        }
        else
        {
            // this means we are already polling for the download to be ready
            ContentLocation = url;
        }

        // poll the download location for the download to be ready
        var readyForDownload = await WaitForDownloadReadyAsync(ContentLocation);
        var jsonResponse = await readyForDownload.Content.ReadAsStringAsync();
        try
        {
            var json = JsonDocument.Parse(jsonResponse);
            var files = json.RootElement.GetProperty("output").EnumerateArray();
            foreach (var file in files)
            {
                var resource = new FileListing
                {
                    Type = file.GetProperty("type").GetString() ?? "",
                    Url = file.GetProperty("url").GetString() ?? "",
                };
                BulkFiles.Add(resource);
            }
        }
        catch (JsonException)
        {
            AnsiConsole.MarkupLine("[bold red]Error: Invalid JSON response from the server.[/]");
            return;
        }

        if (BulkFiles.Count > 0)
        {
            var downloadResult = await DownloadFilesAsync(output);
            if (downloadResult)
            {
                var processResult = await ProcessDownloadedFilesAsync(
                    Path.Combine(output, "staging"),
                    Path.Combine(output, "output.ndjson")
                );
                if (!processResult)
                    AnsiConsole.MarkupLine(
                        "[bold red]Error: Failed to process downloaded files.[/]"
                    );
                else
                    AnsiConsole.MarkupLine(
                        "[bold green]Download and processing completed successfully.[/]"
                    );
            }
        }
    }

    private async Task<HttpResponseMessage> WaitForDownloadReadyAsync(string url)
    {
        var location = ContentLocation ?? url;
        var result = false;
        var res = new HttpResponseMessage();
        var retries = 0;
        do
        {
            var response = await _httpClient.GetAsync(location);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                result = true;
                res = response;
            }
            else if (response.StatusCode == HttpStatusCode.Accepted)
            {
                retries++;
                var waitTime = response.Headers.GetValues("Retry-After").FirstOrDefault();

                if (waitTime != null)
                    WaitTime = int.Parse(waitTime);
                WaitTime *= retries;
                AnsiConsole.MarkupLine($"[bold blue]Retry-After: {WaitTime} seconds.[/]");
                await AnsiConsole
                    .Status()
                    .StartAsync(
                        "Waiting...",
                        async ctx =>
                        {
                            for (var i = 0; i < WaitTime; i++)
                            {
                                await Task.Delay(1000);
                                ctx.Status($"Waiting... {WaitTime - i} seconds");
                                ctx.Spinner(Spinner.Known.Star);
                                ctx.SpinnerStyle(Style.Parse("green"));
                            }
                        }
                    );
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red]Error: {response.StatusCode}[/]");
                response.WriteRequestToConsole();
                retries++;
            }
        } while (!result && retries < MaxRetries);

        return res;
    }

    private async Task<bool> DownloadFilesAsync(string output, int numDl = 5)
    {
        // download and process the files
        var result = false;
        var semaphore = new SemaphoreSlim(numDl);
        var filePath = Path.Combine(output, "staging");
        if (!Directory.Exists(filePath))
            Directory.CreateDirectory(filePath);

        var tasks = BulkFiles
            .Select(async file =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var fileName = Path.Combine(
                        filePath,
                        $"{file.Type}-{file.Url.Split("/").LastOrDefault()}.ndjson"
                    );
                    await DownloadFileAsync(file.Url, Path.Combine(output, "staging", fileName));
                }
                finally
                {
                    semaphore.Release();
                }
            })
            .ToList();
        result = await Task.WhenAll(tasks).ContinueWith(t => t.IsCompletedSuccessfully);
        return result;
    }

    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        // get a new copy of the _httpClient to avoid issues with concurrent requests, but make sure it has the same headers
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Accept", _config.AcceptHeader);
        var content = await client.GetStreamAsync(url);
        await using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write
        );
        await content.CopyToAsync(fileStream);
    }

    private static async Task<bool> ProcessDownloadedFilesAsync(
        string filePath,
        string outputFile,
        int batchSize = 1000
    )
    {
        var result = false;
        var files = Directory.GetFiles(filePath);
        var buffer = new List<string>(batchSize);

        await using var writer = new StreamWriter(outputFile, false);
        var isFirstLine = true;
        foreach (var file in files)
        {
            var fileContent = await File.ReadAllTextAsync(file);
            var json = JsonDocument.Parse(fileContent);
            var base64Resources = json.RootElement.GetProperty("data").GetString();
            var resources = base64Resources
                ?.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Convert.FromBase64String);
            if (resources is null)
                continue;
            foreach (var resource in resources)
            {
                var line = Encoding.UTF8.GetString(resource).Trim();
                buffer.Add(line);
                if (buffer.Count == batchSize)
                {
                    foreach (var bufferLine in buffer)
                    {
                        if (!isFirstLine)
                            await writer.WriteLineAsync();
                        else
                            isFirstLine = false;
                        await writer.WriteAsync(bufferLine);
                    }

                    buffer.Clear();
                }
            }
        }

        if (buffer.Count > 0)
        {
            foreach (var bufferLine in buffer)
            {
                if (!isFirstLine)
                    await writer.WriteLineAsync();
                else
                    isFirstLine = false;
                await writer.WriteAsync(bufferLine);
            }

            buffer.Clear();
        }

        result = true;
        return result;
    }

    private struct FileListing
    {
        public string Type { get; set; }
        public string Url { get; set; }
    }
}

internal static class HttpResponseMessageExtensions
{
    internal static void WriteRequestToConsole(this HttpResponseMessage response)
    {
        if (response is null)
            return;

        var request = response.RequestMessage;
        Console.Write($"{request?.Method} ");
        Console.Write($"{request?.RequestUri} ");
        Console.WriteLine($"HTTP/{request?.Version}");
        Console.WriteLine(response?.StatusCode);
    }
}
