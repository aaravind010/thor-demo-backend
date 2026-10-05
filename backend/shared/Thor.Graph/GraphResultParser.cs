using System.Collections;

namespace Thor.Graph;

/// <summary>
/// Turns the loosely-typed maps Gremlin.Net deserializes from GraphSON into the reader's records,
/// so no caller ever handles a raw driver result.
/// </summary>
internal static class GraphResultParser
{
    private const string TenantIdKey = "tenantId";
    private const string PgIdKey = "pgId";

    /// <summary>GraphSON maps arrive as <c>Dictionary&lt;object, object&gt;</c> or <c>Dictionary&lt;string, object&gt;</c> depending on the payload, so both go through the non-generic view.</summary>
    public static IReadOnlyDictionary<string, object?> ToDictionary(object? value)
    {
        var result = new Dictionary<string, object?>();
        if (value is IDictionary map)
        {
            foreach (DictionaryEntry entry in map)
            {
                result[Convert.ToString(entry.Key)!] = entry.Value;
            }
        }
        return result;
    }

    /// <summary>Drops the partition bookkeeping every vertex and edge carries; callers already know their tenant.</summary>
    public static IReadOnlyDictionary<string, object?> WithoutBookkeeping(IReadOnlyDictionary<string, object?> properties) =>
        properties
            .Where(p => p.Key is not (TenantIdKey or PgIdKey))
            .ToDictionary(p => p.Key, p => p.Value);

    public static GraphVertexView ToVertexView(string label, object? props)
    {
        var properties = ToDictionary(props);
        var id = Guid.Parse(Convert.ToString(properties[PgIdKey])!);
        return new GraphVertexView(label, id, WithoutBookkeeping(properties));
    }
}
