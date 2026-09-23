using System.Net.WebSockets;
using Gremlin.Net.Driver;
using Gremlin.Net.Structure.IO.GraphSON;

namespace Thor.Graph;

/// <summary>
/// Owns the single <see cref="IGremlinClient"/> connection to the shared Neptune cluster,
/// authenticated via IAM/SigV4 (see <see cref="NeptuneSigV4Signer"/>). Internal — nothing outside
/// this library ever touches a raw Gremlin client, only <see cref="IGraphVertexStore"/>/
/// <see cref="IGraphEdgeStore"/>.
/// </summary>
internal sealed class GremlinConnectionFactory(NeptuneOptions options) : IDisposable
{
    private readonly Lazy<IGremlinClient> _client = new(() => CreateClient(options));

    public IGremlinClient Client => _client.Value;

    private static IGremlinClient CreateClient(NeptuneOptions options)
    {
        var server = new GremlinServer(options.Endpoint, options.Port, enableSsl: options.EnableSsl);
        var serializer = new GraphSON2MessageSerializer(new GraphSON2Reader(), new GraphSON2Writer());
        return new GremlinClient(server, serializer, webSocketConfiguration: ws => ConfigureWebSocket(ws, options));
    }

    // Gremlin.Net invokes this once per underlying WebSocket connection it opens (not per
    // traversal call), so every connection/reconnect gets a fresh signature and timestamp.
    private static void ConfigureWebSocket(ClientWebSocketOptions webSocketOptions, NeptuneOptions options)
    {
        var scheme = options.EnableSsl ? "https" : "http";
        var endpoint = new Uri($"{scheme}://{options.Endpoint}:{options.Port}");
        var headers = NeptuneSigV4Signer.SignRequest("GET", endpoint, "/gremlin", options.Region);

        foreach (var (name, value) in headers)
        {
            // Host is sent automatically by ClientWebSocket from the connection URI (which
            // already matches what was signed above) and can't be overridden here.
            if (string.Equals(name, "host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            webSocketOptions.SetRequestHeader(name, value);
        }
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
