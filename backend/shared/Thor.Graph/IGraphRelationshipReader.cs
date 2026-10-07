namespace Thor.Graph;

/// <summary>
/// Read-only relationship traversals against the shared Neptune cluster. Like the write stores,
/// every method takes an explicit <paramref name="tenantId" /> and derives the start vertex address
/// from it, and every vertex a traversal steps onto is also filtered by that tenant — so a read can
/// neither start from nor walk into another tenant's partition (ADR §1/§6.1).
///
/// A start vertex that isn't in the graph (not graph-loaded yet, or wrong tenant) yields an empty
/// page, not an error: callers establish existence from Postgres first.
/// </summary>
public interface IGraphRelationshipReader
{
    /// <summary>Direct neighbours over the given edge labels (all labels when <paramref name="relTypes"/> is empty).</summary>
    Task<GraphPage<GraphNeighbor>> GetNeighborsAsync(
        Guid tenantId, GraphVertexRef start, GraphDirection direction, IReadOnlyCollection<string> relTypes,
        Guid? after, int limit, CancellationToken cancellationToken = default);

    /// <summary>Every vertex reachable by following <paramref name="relType"/> up to <paramref name="maxDepth"/> hops.</summary>
    Task<GraphPage<GraphReachedVertex>> GetReachableAsync(
        Guid tenantId, GraphVertexRef start, string relType, GraphDirection direction, int maxDepth,
        Guid? after, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assets the start vertex can access: its own <c>HAS_ACCESS</c> edges plus those of every group
    /// it is a member of, directly or through up to <paramref name="maxDepth"/> levels of nesting.
    /// </summary>
    Task<GraphPage<GraphAccess>> GetEffectiveAccessAsync(
        Guid tenantId, GraphVertexRef start, int maxDepth, Guid? after, int limit, CancellationToken cancellationToken = default);
}
