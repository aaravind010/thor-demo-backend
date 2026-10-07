using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Remote;
using Gremlin.Net.Process.Traversal;
using static Gremlin.Net.Process.Traversal.AnonymousTraversalSource;

namespace Thor.Graph;

/// <summary>Looks vertices up by tenant-namespaced id in batches, submitted as bytecode like <see cref="GraphRelationshipReader"/>.</summary>
public sealed class GraphVertexExistenceReader : IGraphVertexExistenceReader, IDisposable
{
    private const int BatchSize = 500;

    private readonly Lazy<GraphTraversalSource> _g;
    private readonly IDisposable? _ownedConnection;

    public GraphVertexExistenceReader(NeptuneOptions options)
    {
        var factory = new GremlinConnectionFactory(options);
        _g = new Lazy<GraphTraversalSource>(() => Traversal().With(new DriverRemoteConnection(factory.Client, "g")));
        _ownedConnection = factory;
    }

    /// <summary>Test-only seam — lets tests substitute a fake or local <see cref="IGremlinClient"/> instead of a real Neptune connection.</summary>
    internal GraphVertexExistenceReader(IGremlinClient client)
    {
        _g = new Lazy<GraphTraversalSource>(() => Traversal().With(new DriverRemoteConnection(client, "g")));
        _ownedConnection = null;
    }

    public async Task<IReadOnlySet<GraphVertexRef>> GetExistingAsync(
        Guid tenantId, IReadOnlyCollection<GraphVertexRef> vertices, CancellationToken cancellationToken = default)
    {
        var byVid = vertices.Distinct().ToDictionary(v => GraphIds.VertexId(tenantId, v.EntityType, v.Id));
        var existing = new HashSet<GraphVertexRef>();

        foreach (var batch in byVid.Keys.Chunk(BatchSize))
        {
            var found = await _g.Value.V(batch.Cast<object>().ToArray()).Id()
                .Promise(t => t.ToList(), cancellationToken);
            foreach (var vid in found)
            {
                if (byVid.TryGetValue(Convert.ToString(vid)!, out var vertex))
                {
                    existing.Add(vertex);
                }
            }
        }

        return existing;
    }

    public void Dispose() => _ownedConnection?.Dispose();
}
