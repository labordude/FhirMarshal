using System.Text.Json;

namespace FhirMarshal.Helpers;

public class Transformer
{
    private static readonly Dictionary<
        string,
        Dictionary<string, JsonElement>
    > CachedTransformData = new();

    private static readonly Lock TransformLock = new();

    public static void PreloadTransformData(string fhirVersion)
    {
        lock (TransformLock)
        {
            if (!CachedTransformData.ContainsKey(fhirVersion))
            {
                var transformData = LoadTransformData(fhirVersion);
                CachedTransformData[fhirVersion] = transformData;
            }
        }
    }

    private static Dictionary<string, JsonElement> GetTransformData(string fhirVersion)
    {
        lock (TransformLock)
        {
            if (CachedTransformData.TryGetValue(fhirVersion, out var transformData))
            {
                return transformData;
            }
            throw new Exception($"Transform data for FHIR version {fhirVersion} not found");
        }
    }

    private static Dictionary<string, JsonElement> LoadTransformData(string fhirVersion)
    {
        var fileName = $"fhirbase-import-{fhirVersion}.json";
        var filepath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Assets",
            "transform",
            fileName
        );

        if (!File.Exists(filepath))
        {
            throw new Exception($"Cannot find transformation file {filepath}");
        }

        try
        {
            var trData = File.ReadAllText(filepath);
            if (string.IsNullOrWhiteSpace(trData))
            {
                throw new Exception($"Cannot read transformation file {filepath}");
            }

            var transformation = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                trData
            );
            if (transformation == null)
            {
                throw new Exception($"Cannot deserialize transformation file {filepath}");
            }

            return transformation;
        }
        catch (Exception e)
        {
            throw new Exception($"Cannot load transformation file {filepath}: {e.Message}");
        }
    }

    public static async Task<Dictionary<string, JsonElement>> DoTransformAsync(
        Dictionary<string, JsonElement> resource,
        string fhirVersion
    )
    {
        var transformedData = GetTransformData(fhirVersion);
        if (transformedData == null)
        {
            throw new Exception($"cannot get transformations data for FHIR version {fhirVersion}");
        }

        var resourceType = Bundle.GetResourceType(resource);
        if (resourceType == null)
        {
            throw new Exception($"cannot determine resourceType for resource {resource}");
        }

        var transformationNode = GetTransformationNode(transformedData, resourceType);
        if (transformationNode == null)
        {
            throw new Exception($"cannot find transformation node for resourceType {resourceType}");
        }

        var transformedNodeMap = Transform(resource, transformationNode, transformedData);
        if (transformedNodeMap == null)
        {
            throw new Exception("cannot transform resource");
        }

        return await Task.FromResult(transformedNodeMap);
    }

    private static Dictionary<string, JsonElement> Transform(
        Dictionary<string, JsonElement> resource,
        Dictionary<string, JsonElement> transformationNode,
        Dictionary<string, JsonElement> transformedData
    )
    {
        var transformedValues = new Dictionary<string, JsonElement>();

        // Loop through the resource and apply transformations based on the transformation node
        foreach (var kvp in resource)
        {
            var key = kvp.Key;
            var value = kvp.Value;

            var dictionary = new Dictionary<string, JsonElement> { { key, value } };
            var transformed = ApplyTransformation(
                dictionary,
                value,
                transformationNode,
                transformedData
            );

            // Add the transformed value to the new dictionary
            transformedValues.TryAdd(transformed.Keys.First(), transformed.Values.First());
        }

        return transformedValues;
    }

    private static Dictionary<string, JsonElement> ApplyTransformation(
        Dictionary<string, JsonElement> kvp,
        JsonElement node,
        Dictionary<string, JsonElement>? transformationNode,
        Dictionary<string, JsonElement> transformedData
    )
    {
        // check to see if transformation has a property tr/act
        var thisKey = kvp.Keys.First();

        if (
            transformationNode != null
            && transformationNode.ContainsKey(thisKey)
            && transformationNode[thisKey].TryGetProperty("tr/act", out var action)
        )
        {
            var thisTransformationNode = transformationNode[thisKey];
            if (action.GetString() is "union")
            {
                return UnionTransformation(
                    kvp,
                    thisTransformationNode,
                    transformationNode,
                    transformedData
                );
            }
            else if (action.GetString() is "reference")
            {
                var key = kvp.Keys.First();
                var references = Reference.GetReferenceValues(kvp[key]);

                if (
                    transformationNode.TryGetValue("isCollection", out var isCollection)
                    && isCollection.ValueKind != JsonValueKind.True
                )
                {
                    if (references.Count > 1)
                    {
                        // get the first Reference and make it not a collection

                        kvp[key] = JsonSerializer.Deserialize<JsonElement>(
                            JsonSerializer.Serialize(references[0])
                        );
                    }
                    else
                    {
                        var first = references.FirstOrDefault();
                        kvp[key] = JsonSerializer.Deserialize<JsonElement>(
                            JsonSerializer.Serialize(first)
                        );
                    }
                }
                else
                {
                    kvp[key] = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(references)
                    );
                }
                return kvp;
            }
        }

        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                var result = new Dictionary<string, JsonElement>();

                foreach (var item in node.EnumerateObject())
                {
                    var itemKey = item.Name;
                    var itemValue = item.Value;
                    if (
                        transformationNode != null
                        && transformationNode.TryGetValue(itemKey, out var nodeStep)
                    )
                    {
                        // walk the node down
                        var nextTransformationNode = new Dictionary<string, JsonElement>
                        {
                            { itemKey, nodeStep },
                        };

                        // check if the next transformation node has a tr/act property
                        JsonElement? args = nextTransformationNode.TryGetValue(
                            "tr/arg",
                            out var arg
                        )
                            ? arg
                            : null;

                        // set the key to the current item key
                        var key = itemKey;

                        // is this a valid object?
                        if (
                            args is
                            { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) }
                        )
                        {
                            if (
                                args.Value.TryGetProperty("key", out var keyProp)
                                && keyProp.ValueKind == JsonValueKind.String
                            )
                            {
                                key = keyProp.GetString() ?? key;
                            }
                        }

                        // check if the next transformation node has a tr/move property
                        if (nextTransformationNode.TryGetValue("tr/move", out var move))
                        {
                            // get the next transformation node
                            nextTransformationNode = GetByPath(transformedData, move);
                        }
                        var newKvp = new Dictionary<string, JsonElement> { { key, itemValue } };
                        var res = ApplyTransformation(
                            newKvp,
                            itemValue,
                            nextTransformationNode,
                            transformedData
                        );
                        result = res;
                    }
                    else
                    {
                        result[itemKey] = itemValue;
                    }
                }
                return result;

            case JsonValueKind.Array:
                var array = new List<Dictionary<string, JsonElement>>();
                foreach (var item in node.EnumerateArray())
                {
                    var itemKvp = new Dictionary<string, JsonElement> { { thisKey, item } };

                    var transformedItem = ApplyTransformation(
                        itemKvp,
                        item,
                        transformationNode,
                        transformedData
                    );

                    array.Add(transformedItem);
                }
                kvp = new Dictionary<string, JsonElement>
                {
                    {
                        thisKey,
                        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(array))
                    },
                };

                break;
        }
        return kvp;
    }

    private static Dictionary<string, JsonElement> UnionTransformation(
        Dictionary<string, JsonElement> kvp,
        JsonElement? transformation,
        Dictionary<string, JsonElement> transformationNode,
        Dictionary<string, JsonElement> transformedData
    )
    {
        var thisKey = kvp.Keys.First();
        var thisNode = transformationNode[thisKey];
        if (!thisNode.TryGetProperty("tr/arg", out var args) && transformation == null)
        {
            throw new Exception("cannot find transformation node for union");
        }
        if (!args.TryGetProperty("type", out var type))
        {
            throw new Exception("cannot find type in union transformation");
        }
        if (type.ValueKind != JsonValueKind.String || type.GetString() == null)
        {
            throw new Exception("type must be a string");
        }

        Dictionary<string, JsonElement> transformed;
        string? typeString = type.GetString() ?? null;
        if (typeString != null && transformedData.TryGetValue(typeString, out var typeData))
        {
            if (type.GetString() is "Reference")
            {
                var referenceTransform = ApplyTransformation(
                    kvp,
                    typeData,
                    new Dictionary<string, JsonElement>
                    {
                        { "tr/act", JsonDocument.Parse("\"reference\"").RootElement },
                    },
                    transformedData
                );
                transformed = referenceTransform;
            }
            else
            {
                var typeTransformationNode = GetTransformationNode(transformedData, typeString);
                if (typeTransformationNode == null)
                {
                    throw new Exception($"cannot find transformation node for type {typeString}");
                }

                var result = ApplyTransformation(
                    kvp,
                    typeData,
                    typeTransformationNode,
                    transformedData
                );
                transformed = result;
            }
        }
        else
        {
            var newKey = args.TryGetProperty("key", out var key)
                ? key.GetString() ?? thisKey
                : thisKey;
            var value = kvp[thisKey];
            var nestedObject = new Dictionary<string, JsonElement>
            {
                { type.GetString() ?? thisKey, value },
            };

            transformed = new Dictionary<string, JsonElement>
            {
                { newKey, JsonDocument.Parse(JsonSerializer.Serialize(nestedObject)).RootElement },
            };
        }
        return transformed;
    }

    private static Dictionary<string, JsonElement>? GetByPath(
        Dictionary<string, JsonElement> transformedData,
        JsonElement path
    )
    {
        var currentNode = transformedData;

        // Convert JsonElement path to List<string>
        if (path.ValueKind != JsonValueKind.Array)
        {
            throw new Exception("Path must be a JSON array.");
        }

        var pathList = path.EnumerateArray().Select(element => element.GetString()).ToList();

        foreach (var segment in pathList)
        {
            if (segment == null)
            {
                throw new Exception($"Invalid path segment: {segment}");
            }

            if (!currentNode.TryGetValue(segment, out var nextNode))
            {
                Console.WriteLine($"Cannot find key '{segment}' in the current node.");
                return null;
            }

            if (nextNode.ValueKind != JsonValueKind.Null)
            {
                // If it's not a valid object, handle it as needed (you might want to throw an error or handle other cases)
                Console.WriteLine(
                    $"Unexpected JsonElement type at key '{segment}': {nextNode.ValueKind}"
                );
                return null;
            }

            currentNode = Bundle.JsonElementToDictionary(nextNode);
        }

        return currentNode;
    }

    private static Dictionary<string, JsonElement> GetTransformationNode(
        Dictionary<string, JsonElement> parentNode,
        string key
    )
    {
        if (parentNode.TryGetValue(key, out var node))
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                return Bundle.JsonElementToDictionary(node);
            }
        }

        return new Dictionary<string, JsonElement>();
    }
}
