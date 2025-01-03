using System.Text;
using System.Text.Json;
using FhirMarshal.Models;

namespace FhirMarshal.Helpers;

public class Bundle
{
    public static Dictionary<string, object?> NormalizeJsonElement(
        Dictionary<string, object?> original
    )
    {
        var normalized = new Dictionary<string, object?>();

        foreach (var kvp in original)
            if (kvp.Value is JsonElement jsonElement)
                normalized[kvp.Key] = ConvertJsonElement(jsonElement);
            else
                normalized[kvp.Key] = kvp.Value;

        return normalized;
    }

    private static object? ConvertJsonElement(JsonElement jsonElement)
    {
        return jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement.TryGetInt64(out var longValue)
                ? longValue
                : jsonElement.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object>>(
                jsonElement.GetRawText()
            ),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object>>(
                jsonElement.GetRawText()
            ),
            _ => jsonElement.GetRawText(),
        };
    }

    public static string GetResourceId(Dictionary<string, JsonElement> resource)
    {
        if (resource.ContainsKey("id"))
        {
            var value = resource["id"];
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            Console.WriteLine("id is not a string or is null.");
        }
        else
        {
            Console.WriteLine("id not found in resource.");
        }

        return "";
    }

    public static string GetResourceType(Dictionary<string, JsonElement> resource)
    {
        if (resource.ContainsKey("resourceType"))
        {
            var value = resource["resourceType"];
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            Console.WriteLine("resourceType is not a string or is null.");
        }
        else
        {
            Console.WriteLine("resourceType not found in resource.");
        }

        return "";
    }

    public static BundleType GetBundleType(Stream inputStream)
    {
        using var reader = new StreamReader(inputStream);
        var firstLine = reader.ReadLine();

        if (string.IsNullOrEmpty(firstLine))
            return BundleType.UnknownBundleType; // the file is empty

        try
        {
            if (reader.EndOfStream)
            {
                using var singleLineStream = new MemoryStream(Encoding.UTF8.GetBytes(firstLine));
                return GuessJsonBundleType(singleLineStream);
            }

            var secondLine = reader.ReadLine();
            if (string.IsNullOrEmpty(secondLine))
                return BundleType.UnknownBundleType; // the file is malformed

            if (IsCompleteJsonObject(firstLine) && IsCompleteJsonObject(secondLine))
                return BundleType.Ndjson;

            var combinedStream = new MemoryStream();
            using (var writer = new StreamWriter(combinedStream, leaveOpen: true))
            {
                writer.WriteLine(firstLine);
                writer.WriteLine(secondLine);
                writer.Flush();

                inputStream.CopyTo(combinedStream);
            }

            combinedStream.Seek(0, SeekOrigin.Begin);
            return GuessJsonBundleType(combinedStream);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error determining bundle type: {e.Message}");
            return BundleType.UnknownBundleType;
        }
    }

    private static bool IsCompleteJsonObject(string line)
    {
        try
        {
            JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static BundleType GuessJsonBundleType(Stream inputStream)
    {
        using var json = JsonDocument.Parse(inputStream);
        var rootElement = json.RootElement;
        if (rootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Expecting to get a JSON object at the root");

        foreach (var property in rootElement.EnumerateObject())
            if (property.Name == "resourceType")
            {
                var resourceType = property.Value.GetString();

                if (resourceType == "Bundle")
                    return BundleType.Fhir;

                if (!string.IsNullOrEmpty(resourceType))
                    return BundleType.SingleResource;

                return BundleType.UnknownBundleType;
            }

        return BundleType.Fhir;
    }

    public static Dictionary<string, JsonElement> JsonElementToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expected JsonValueKind.Object", nameof(element));

        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
            result[property.Name] = property.Value; // Avoid extra allocations

        return result;
    }
}
