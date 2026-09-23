namespace Thor.Graph;

/// <summary>
/// Idempotent edge upserts against the shared Neptune cluster. See <see cref="IGraphVertexStore"/>
/// for the tenant-isolation rationale — the same structural guarantee applies here.
/// </summary>
public interface IGraphEdgeStore
{
    Task UpsertEdgeAsync(
        Guid tenantId, string relType, GraphVertexRef from, GraphVertexRef to,
        IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default);

    Task DeleteEdgeAsync(
        Guid tenantId, string relType, GraphVertexRef from, GraphVertexRef to, CancellationToken cancellationToken = default);
}
