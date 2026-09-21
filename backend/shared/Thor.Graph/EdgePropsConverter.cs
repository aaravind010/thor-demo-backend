using System.Text.Json;

namespace Thor.Graph;

/// <summary>
/// Converts an edge's stored <c>props</c> JSON-object string (see
/// <c>Thor.DataLayer.Models.Tenants.Edge.Props</c>) into the flat property dictionary both graph
/// write paths need — <see cref="IGraphEdgeStore.UpsertEdgeAsync"/>'s per-item Gremlin call and
/// the bulk CSV writer. Unwraps JSON values to plain CLR primitives (bool/long/double/string) so
/// callers never hand a <see cref="JsonElement"/> to the Gremlin driver or a CSV formatter, neither
/// of which know how to serialize one. Props are expected to be a flat object (e.g. a permission
/// set) — a nested object/array value is preserved as its raw JSON text rather than expanded.
/// </summary>
public static class EdgePropsConverter
{
    public static IReadOnlyDictionary<string, object?> ToPropertyDictionary(string? propsJson)
    {
        if (string.IsNullOrEmpty(propsJson))
        {
            return new Dictionary<string, object?>();
        }

        using var doc = JsonDocument.Parse(propsJson);
        var result = new Dictionary<string, object?>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };
        }
        return result;
    }
}
