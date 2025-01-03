using System.Text.Json;

namespace FhirMarshal.Models;

public class SingleResourceBundle : IBundle
{
    private readonly BundleFile _file;
    private bool _alreadyRead;

    public SingleResourceBundle(BundleFile file)
    {
        _file = file;
        _alreadyRead = false;
    }

    public int Count { get; set; } = 1;

    public (Dictionary<string, JsonElement> Item, Exception? Error) Next()
    {
        if (_alreadyRead)
            return (
                new Dictionary<string, JsonElement>(),
                new Exception("No more items in the bundle")
            );
        // try to parse the file as json
        // if it fails, return an error

        try
        {
            //parse the file into Dictionary<string, object>
            var json = JsonDocument.Parse(_file.File ?? throw new InvalidOperationException());
            var root = json.RootElement;
            var item = new Dictionary<string, JsonElement>();
            foreach (var property in root.EnumerateObject())
                item[property.Name] = property.Value;
            _alreadyRead = true;
            return (item, null);
        }
        catch (JsonException e)
        {
            return (
                new Dictionary<string, JsonElement>(),
                new Exception($"Error parsing json: {e.Message}")
            );
        }
        catch (Exception)
        {
            return (
                new Dictionary<string, JsonElement>(),
                new Exception("No more items in the bundle")
            );
        }
    }

    public void Close()
    {
        _file.File?.Close();
    }
}
