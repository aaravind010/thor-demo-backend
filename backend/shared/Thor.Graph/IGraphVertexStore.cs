namespace Thor.Graph;

/// <summary>
/// Idempotent vertex upserts against the shared Neptune cluster. Every method requires an
/// explicit <paramref name="tenantId" /> and derives the physical vertex address internally —
/// there is no way to address a vertex without one (structural tenant isolation, ADR §1/§6.1).
/// </summary>
public interface IGraphVertexStore
{
    Task UpsertVertexAsync(
        Guid tenantId, string entityType, Guid id, IReadOnlyDictionary<string, object?> properties,
        CancellationToken cancellationToken = default);

    Task UpsertVerticesAsync(
        Guid tenantId, string entityType, IReadOnlyList<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> vertices,
        CancellationToken cancellationToken = default);

    Task DeleteVertexAsync(Guid tenantId, string entityType, Guid id, CancellationToken cancellationToken = default);
}
