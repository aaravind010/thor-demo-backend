using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.AttributeMapping;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// Shared "look up which raw attribute feeds a column, then unwrap it" logic every normalizer
/// needs. The lookup wrapper is identical across all three connectors; only the unwrap
/// convention differs — AD's LDAP array-wrapped single-valued attributes vs. CyberArk's/
/// Windows' plain values (<see cref="AsString"/>).
/// </summary>
public static class MappedFieldReader
{
    public static string? Get(JsonObject raw, AttributeMap map, string column, Func<JsonNode?, string?> unwrap) =>
        map.ColumnMappings.TryGetValue(column, out var attributeName) ? unwrap(raw[attributeName]) : null;

    public static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s) ? s : null;

    public static bool GetBool(JsonObject? obj, string key) =>
        obj?[key] is JsonValue value && value.TryGetValue<bool>(out var b) && b;
}
