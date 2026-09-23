using System.Text;

namespace Thor.Graph.BulkLoad;

/// <summary>
/// Formats vertex/edge upsert rows into Neptune's Gremlin CSV bulk-load format (one file per
/// entity "shape", since every row in a CSV file shares one header). Reuses the same
/// tenant-namespaced id scheme <see cref="GraphVertexStore"/>/<see cref="GraphEdgeStore"/> use
/// for their own direct (non-bulk-load) vertex/edge deletes, so a bulk-loaded vertex coalesces
/// with (rather than duplicates) anything already in the graph.
///
/// Column types/cardinality in the header follow AWS's documented Gremlin CSV format
/// (<c>name:Type(single)</c>) — verify against current Neptune bulk-loader docs before relying
/// on this against a real cluster, since the exact set of supported type tokens isn't something
/// this was checked against a live endpoint.
/// </summary>
public static class GremlinCsvWriter
{
    /// <summary>
    /// Writes one vertex-label's rows. All <paramref name="vertices"/> must share the same
    /// property key set (true for every caller today — each entity type's property projection is
    /// a fixed shape) since the CSV header is derived from the first row.
    /// </summary>
    public static string WriteVertexCsv(
        Guid tenantId, string entityType,
        IReadOnlyList<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> vertices)
    {
        if (vertices.Count == 0)
        {
            return string.Empty;
        }

        var propertyKeys = vertices[0].Properties.Keys.ToList();
        var propertyKeySet = new HashSet<string>(propertyKeys);
        foreach (var (id, properties) in vertices)
        {
            if (!properties.Keys.ToHashSet().SetEquals(propertyKeySet))
            {
                throw new InvalidOperationException(
                    $"Vertex {id} has a different property key set than the first row in this batch " +
                    $"(expected: {string.Join(", ", propertyKeys)}; actual: {string.Join(", ", properties.Keys)}).");
            }
        }

        var sb = new StringBuilder();

        sb.Append("~id,~label,tenantId:String(single),pgId:String(single)");
        foreach (var key in propertyKeys)
        {
            var sample = vertices.Select(v => v.Properties.GetValueOrDefault(key)).FirstOrDefault(v => v is not null);
            AppendHeaderColumn(sb, key, sample);
        }
        sb.Append('\n');

        foreach (var (id, properties) in vertices)
        {
            var vid = GraphIds.VertexId(tenantId, entityType, id);
            sb.Append(EscapeCsv(vid)).Append(',').Append(EscapeCsv(entityType));
            sb.Append(',').Append(EscapeCsv(tenantId.ToString())).Append(',').Append(EscapeCsv(id.ToString()));
            foreach (var key in propertyKeys)
            {
                sb.Append(',').Append(EscapeCsv(FormatValue(properties.GetValueOrDefault(key))));
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes an edge file. Unlike <see cref="WriteVertexCsv"/> — whose callers all project a
    /// fixed, per-type property shape — an edge's <c>props</c> is a free-form JSON blob that can
    /// vary per relationship type (a plain <c>MEMBER_OF</c> has none; a <c>HAS_ACCESS</c> edge
    /// carries a permission set), so the header is the *union* of every edge's property keys in
    /// this batch rather than just the first row's, and a given row leaves a column empty if its
    /// own <c>Properties</c> doesn't have that key.
    /// </summary>
    public static string WriteEdgeCsv(
        Guid tenantId,
        IReadOnlyList<(Guid EdgeId, string RelType, GraphVertexRef From, GraphVertexRef To, IReadOnlyDictionary<string, object?> Properties)> edges)
    {
        if (edges.Count == 0)
        {
            return string.Empty;
        }

        var propertyKeys = edges.SelectMany(e => e.Properties.Keys).Distinct().ToList();

        var sb = new StringBuilder();
        sb.Append("~id,~from,~to,~label,tenantId:String(single)");
        foreach (var key in propertyKeys)
        {
            var sample = edges.Select(e => e.Properties.GetValueOrDefault(key)).FirstOrDefault(v => v is not null);
            AppendHeaderColumn(sb, key, sample);
        }
        sb.Append('\n');

        foreach (var (edgeId, relType, from, to, properties) in edges)
        {
            var eid = GraphIds.EdgeId(tenantId, edgeId);
            var fromVid = GraphIds.VertexId(tenantId, from.EntityType, from.Id);
            var toVid = GraphIds.VertexId(tenantId, to.EntityType, to.Id);
            sb.Append(EscapeCsv(eid)).Append(',').Append(EscapeCsv(fromVid)).Append(',').Append(EscapeCsv(toVid));
            sb.Append(',').Append(EscapeCsv(relType)).Append(',').Append(EscapeCsv(tenantId.ToString()));
            foreach (var key in propertyKeys)
            {
                sb.Append(',').Append(EscapeCsv(FormatValue(properties.GetValueOrDefault(key))));
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string CsvType(object? sample) => sample switch
    {
        bool => "Bool",
        int or long or short => "Int",
        _ => "String",
    };

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void AppendHeaderColumn(StringBuilder sb, string key, object? sample)
    {
        if (key.Contains(':'))
        {
            throw new InvalidOperationException(
                $"Property key '{key}' contains ':', which Neptune's bulk-load header format reserves as the name/type delimiter.");
        }
        sb.Append(',').Append(EscapeCsv($"{key}:{CsvType(sample)}(single)"));
    }
}
