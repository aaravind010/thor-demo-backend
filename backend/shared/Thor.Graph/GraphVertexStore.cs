using System.Text;
using Gremlin.Net.Driver;

namespace Thor.Graph;

public sealed class GraphVertexStore : IGraphVertexStore, IDisposable
{
    private readonly IGremlinClient _client;
    private readonly IDisposable? _ownedConnection;

    public GraphVertexStore(NeptuneOptions options)
    {
        var factory = new GremlinConnectionFactory(options);
        _client = factory.Client;
        _ownedConnection = factory;
    }

    /// <summary>Test-only seam — lets tests substitute a fake <see cref="IGremlinClient"/> instead of a real Neptune connection.</summary>
    internal GraphVertexStore(IGremlinClient client)
    {
        _client = client;
        _ownedConnection = null;
    }

    public Task UpsertVertexAsync(
        Guid tenantId, string entityType, Guid id, IReadOnlyDictionary<string, object?> properties) =>
        UpsertVerticesAsync(tenantId, entityType, [(id, properties)]);

    public async Task UpsertVerticesAsync(
        Guid tenantId, string entityType, IReadOnlyList<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> vertices)
    {
        foreach (var (id, properties) in vertices)
        {
            var vid = GraphIds.VertexId(tenantId, entityType, id);
            var bindings = new Dictionary<string, object>
            {
                ["vid"] = vid,
                ["vlabel"] = entityType,
                ["tenantIdProp"] = tenantId.ToString(),
                ["pgIdProp"] = id.ToString(),
            };

            var script = new StringBuilder(
                "g.V(vid).fold().coalesce(unfold(), addV(vlabel).property(id, vid))" +
                ".property(single, 'tenantId', tenantIdProp).property(single, 'pgId', pgIdProp)");

            AppendProperties(script, bindings, properties);

            // Property KEYS are interpolated into the script (Gremlin has no bytecode-level way
            // to parameterize a property key in script-submission mode) — safe because keys come
            // only from GraphSync's own controlled column-mapping, never from tenant/user data.
            // Values are always bound parameters.
            await _client.SubmitAsync<dynamic>(script.ToString(), bindings);
        }
    }

    public async Task DeleteVertexAsync(Guid tenantId, string entityType, Guid id)
    {
        var vid = GraphIds.VertexId(tenantId, entityType, id);
        await _client.SubmitAsync<dynamic>("g.V(vid).drop()", new Dictionary<string, object> { ["vid"] = vid });
    }

    private static void AppendProperties(StringBuilder script, Dictionary<string, object> bindings, IReadOnlyDictionary<string, object?> properties)
    {
        var i = 0;
        foreach (var (key, value) in properties)
        {
            var paramName = $"p{i++}";
            bindings[paramName] = value ?? "";
            script.Append(".property(single, '").Append(key).Append("', ").Append(paramName).Append(')');
        }
    }

    public void Dispose() => _ownedConnection?.Dispose();
}
