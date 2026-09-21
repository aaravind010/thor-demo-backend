using System.Text;
using Gremlin.Net.Driver;

namespace Thor.Graph;

public sealed class GraphEdgeStore : IGraphEdgeStore, IDisposable
{
    private readonly IGremlinClient _client;
    private readonly IDisposable? _ownedConnection;

    public GraphEdgeStore(NeptuneOptions options)
    {
        var factory = new GremlinConnectionFactory(options);
        _client = factory.Client;
        _ownedConnection = factory;
    }

    /// <summary>Test-only seam — lets tests substitute a fake <see cref="IGremlinClient"/> instead of a real Neptune connection.</summary>
    internal GraphEdgeStore(IGremlinClient client)
    {
        _client = client;
        _ownedConnection = null;
    }

    public async Task UpsertEdgeAsync(
        Guid tenantId, string relType, GraphVertexRef from, GraphVertexRef to,
        IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
    {
        var bindings = new Dictionary<string, object>
        {
            ["fromVid"] = GraphIds.VertexId(tenantId, from.EntityType, from.Id),
            ["toVid"] = GraphIds.VertexId(tenantId, to.EntityType, to.Id),
            ["relType"] = relType,
            ["tenantIdProp"] = tenantId.ToString(),
        };

        var script = new StringBuilder(
            "g.V(fromVid).as('a').V(toVid).as('b')" +
            ".coalesce(inE(relType).where(outV().as('a')).where(inV().as('b')), addE(relType).from('a').to('b'))" +
            ".property(single, 'tenantId', tenantIdProp)");

        var i = 0;
        foreach (var (key, value) in properties)
        {
            var paramName = $"p{i++}";
            bindings[paramName] = value ?? "";
            script.Append(".property(single, '").Append(key).Append("', ").Append(paramName).Append(')');
        }

        // See GraphVertexStore for the same note on property-key interpolation vs. bound values.
        await _client.SubmitAsync<dynamic>(script.ToString(), bindings, cancellationToken);
    }

    public async Task DeleteEdgeAsync(
        Guid tenantId, string relType, GraphVertexRef from, GraphVertexRef to, CancellationToken cancellationToken = default)
    {
        var bindings = new Dictionary<string, object>
        {
            ["fromVid"] = GraphIds.VertexId(tenantId, from.EntityType, from.Id),
            ["toVid"] = GraphIds.VertexId(tenantId, to.EntityType, to.Id),
            ["relType"] = relType,
        };

        await _client.SubmitAsync<dynamic>(
            "g.V(fromVid).outE(relType).where(inV().hasId(toVid)).drop()", bindings, cancellationToken);
    }

    public void Dispose() => _ownedConnection?.Dispose();
}
