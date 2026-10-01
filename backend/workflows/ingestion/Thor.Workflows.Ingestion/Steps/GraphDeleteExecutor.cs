using Thor.Graph;

namespace Thor.Workflows.Ingestion.Steps;

internal sealed record PendingVertexDelete(string EntityType, Guid Id);

internal sealed record PendingEdgeDelete(string RelType, string FromEntityType, Guid FromId, string ToEntityType, Guid ToId);

/// <summary>
/// Executes vertex/edge deletes directly against Neptune. Shared by <see cref="GraphLoadStartStep"/>'s
/// delete-only path (nothing queued to bulk-load, so deletes are safe to apply immediately) and
/// <see cref="GraphLoadPollStep"/>'s deferred path (deletes queued alongside a bulk load, applied
/// only once that load is confirmed complete).
/// </summary>
internal static class GraphDeleteExecutor
{
    private const int MaxDegreeOfParallelism = 4;

    public static async Task<int> DeleteVerticesAsync(
        IGraphVertexStore vertexStore, Guid tenantId, IReadOnlyList<PendingVertexDelete> toDelete, CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(toDelete, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = cancellationToken },
            async (item, ct) => await vertexStore.DeleteVertexAsync(tenantId, item.EntityType, item.Id, ct));
        return toDelete.Count;
    }

    public static async Task<int> DeleteEdgesAsync(
        IGraphEdgeStore edgeStore, Guid tenantId, IReadOnlyList<PendingEdgeDelete> toDelete, CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(toDelete, new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = cancellationToken },
            async (item, ct) => await edgeStore.DeleteEdgeAsync(
                tenantId, item.RelType, new GraphVertexRef(item.FromEntityType, item.FromId), new GraphVertexRef(item.ToEntityType, item.ToId), ct));
        return toDelete.Count;
    }
}
