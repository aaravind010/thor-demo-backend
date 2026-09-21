namespace Thor.Graph;

/// <summary>
/// Tenant-namespaced Gremlin vertex id — puts the tenant partition in the vertex's address
/// itself (not just a filterable property), so a caller structurally cannot address a vertex
/// without a tenant id. See ADR §6.1 (single shared, tenant-partitioned Neptune cluster).
/// </summary>
internal static class GraphIds
{
    public static string VertexId(Guid tenantId, string entityType, Guid id) => $"{tenantId:N}:{entityType}:{id:N}";

    /// <summary>
    /// Tenant-namespaced Gremlin edge id, keyed off the canonical Postgres <c>tenant.edge.id</c>
    /// — needed only by the Neptune bulk loader path (<see cref="BulkLoad.GremlinCsvWriter"/>),
    /// since its CSV format requires an explicit <c>~id</c> per edge. The per-item
    /// <see cref="GraphEdgeStore"/> path never sets an edge id explicitly (Neptune assigns one),
    /// so re-running a bulk load for the same canonical edge row stays idempotent by construction
    /// even though the two write paths don't share an id scheme.
    /// </summary>
    public static string EdgeId(Guid tenantId, Guid edgeId) => $"{tenantId:N}:edge:{edgeId:N}";
}
