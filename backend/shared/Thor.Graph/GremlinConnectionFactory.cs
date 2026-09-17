using Gremlin.Net.Driver;
using Gremlin.Net.Structure.IO.GraphSON;

namespace Thor.Graph;

/// <summary>
/// Owns the single <see cref="IGremlinClient"/> connection to the shared Neptune cluster.
/// Internal — nothing outside this library ever touches a raw Gremlin client, only
/// <see cref="IGraphVertexStore"/>/<see cref="IGraphEdgeStore"/>. This is also the one seam a
/// future IAM/SigV4 auth mode would extend (a signed <c>webSocketConfiguration</c> passed to
/// <see cref="GremlinClient"/>) without touching any public call site.
/// </summary>
internal sealed class GremlinConnectionFactory(NeptuneOptions options) : IDisposable
{
    private readonly Lazy<IGremlinClient> _client = new(() => CreateClient(options));

    public IGremlinClient Client => _client.Value;

    private static IGremlinClient CreateClient(NeptuneOptions options)
    {
        var server = new GremlinServer(options.Endpoint, options.Port, enableSsl: options.EnableSsl);
        var serializer = new GraphSON2MessageSerializer(new GraphSON2Reader(), new GraphSON2Writer());
        return new GremlinClient(server, serializer);
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
