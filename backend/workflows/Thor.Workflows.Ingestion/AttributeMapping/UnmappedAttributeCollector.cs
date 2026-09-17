using System.Text.Json.Nodes;

namespace Thor.Workflows.Ingestion.AttributeMapping;

/// <summary>
/// Collects every raw attribute a connector's column map didn't consume, for connectors with no
/// existing hand-curated raw-attributes shape to preserve (AD's <c>AccountRawAttributes</c>/
/// <c>GroupRawAttributes</c> predate this and stay as they are). This is what actually delivers
/// "an unmapped raw attribute is stored as JSON in raw_attributes" generically, rather than one
/// connector's curated subset of extra fields.
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
