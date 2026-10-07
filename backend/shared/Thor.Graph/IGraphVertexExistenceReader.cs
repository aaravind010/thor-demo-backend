namespace Thor.Graph;

/// <summary>Reports which vertices already exist in the shared Neptune cluster; tenant-scoped like every other graph store.</summary>
public interface IGraphVertexExistenceReader
{
    Task<IReadOnlySet<GraphVertexRef>> GetExistingAsync(
        Guid tenantId, IReadOnlyCollection<GraphVertexRef> vertices, CancellationToken cancellationToken = default);
}
