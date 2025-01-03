using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FhirMarshal.Models;

public class NdJsonBundle : IBundle
{
    private readonly BundleFile _file;
    private readonly StreamReader? _reader;
    private int _currentLine;

    public NdJsonBundle(BundleFile file)
    {
        _file = file;
        if (file.File != null)
            try
            {
                var gzipStream = new GZipStream(file.File, CompressionMode.Decompress);
                _reader = new StreamReader(gzipStream, Encoding.UTF8); // Explicit encoding

                if (_reader.Peek() == -1)
                    throw new InvalidDataException("May not be gzip or empty");
            }
            catch (InvalidDataException) // Not a gzip file
            {
                file.File.Seek(0, SeekOrigin.Begin);
                _reader = new StreamReader(file.File, Encoding.UTF8);
            }
        else
        {
            _reader = null;
        }

        _currentLine = 0;
        if (_reader != null)
            try
            {
                while (_reader.ReadLine() != null)
                    Count++;

                _file?.File?.Seek(0, SeekOrigin.Begin);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Error reading lines from file: {exception.Message}",
                    exception
                );
            }
    }

    public int Count { get; set; }

    public (Dictionary<string, JsonElement> Item, Exception? Error) Next()
    {
        if (_reader == null)
            return (
                new Dictionary<string, JsonElement>(),
                new Exception("No more items in the bundle")
            );

        var line = _reader.ReadLine();
        if (line == null)
            return (
                new Dictionary<string, JsonElement>(),
                new Exception("No more items in the bundle")
            );

        _currentLine++;
        line = line.Trim();
        // Assuming line is a valid NDJSON string that can be deserialized

        try
        {
            var item = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);

            return (item ?? new Dictionary<string, JsonElement>(), null);
        }
        catch (Exception ex)
        {
            return (
                new Dictionary<string, JsonElement>(),
                new Exception($"Error deserializing line {_currentLine}: {ex.Message}")
            );
        }
    }

    public void Close()
    {
        _reader?.Close();
    }
}
