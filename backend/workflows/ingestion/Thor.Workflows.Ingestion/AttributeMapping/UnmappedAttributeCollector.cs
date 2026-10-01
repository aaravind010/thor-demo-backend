using System.Text.Json.Nodes;

namespace Thor.Workflows.Ingestion.AttributeMapping;

/// <summary>
/// Collects every raw attribute a connector's column map didn't consume, for storage in
/// <c>raw_attributes</c>.
/// </summary>
public static class UnmappedAttributeCollector
{
    public static IReadOnlyDictionary<string, JsonNode?> Collect(JsonObject raw, AttributeMap map)
    {
        var mappedAttributeNames = new HashSet<string>(map.ColumnMappings.Values);
        return raw
            .Where(kv => !mappedAttributeNames.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone());
    }
}
