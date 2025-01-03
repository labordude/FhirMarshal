using System.Text.Json;

namespace FhirMarshal.Helpers;

public class Reference
{
    public string? Id { get; set; }
    public string? ResourceType { get; set; }
    public string? Display { get; set; }

    public static List<Reference> GetReferenceValues(JsonElement referenceElement)
    {
        var values = new List<Reference>();

        if (referenceElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in referenceElement.EnumerateArray())
            {
                values.Add(TransformReference(item)); // Transform to a Reference object
            }
        }
        else
        {
            values.Add(TransformReference(referenceElement)); // Single Reference object
        }

        return values;
    }

    public static Reference TransformReference(JsonElement referenceElement)
    {
        var refString = referenceElement.TryGetProperty("reference", out var reference)
            ? reference.GetString()
            : null;

        var refDisplay = referenceElement.TryGetProperty("display", out var display)
            ? display.GetString()
            : null;
        if (string.IsNullOrEmpty(refString) && !string.IsNullOrEmpty(refDisplay))
        {
            // see if the display string looks like a reference
            if (refDisplay.Contains("/"))
            {
                return new Reference()
                {
                    Display = refDisplay,
                    Id = refDisplay.Split('/')[1],
                    ResourceType = refDisplay.Split('/')[0],
                };
            }
            return new Reference() { Display = refDisplay };
        }

        var idString =
            refString?.Split('/').Length > 1 ? refString?.Split('/')[1]
            : refDisplay?.Split('/').Length > 1 ? refDisplay?.Split('/')[1]
            : null;
        var resourceType = refString?.Split('/')[0] ?? refDisplay?.Split('/')[0] ?? "";
        return new Reference()
        {
            Id = idString ?? "",
            ResourceType = resourceType ?? "",
            Display = refDisplay ?? "",
        };
    }
}
