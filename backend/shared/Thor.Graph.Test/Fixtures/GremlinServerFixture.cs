using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gremlin.Net.Driver;
using Gremlin.Net.Structure.IO.GraphSON;

namespace Thor.Graph.Test.Fixtures;

/// <summary>
/// A throwaway TinkerPop Gremlin Server. TinkerGraph is not Neptune — it runs the same traversal
/// semantics but none of Neptune's query engine — so this proves the traversals are right, not that
/// Neptune executes them identically; a smoke test against dev Neptune still covers that.
///
/// Held as an <see cref="IClassFixture{T}"/>: one container per test class.
/// </summary>
public sealed class GremlinServerFixture : IAsyncLifetime
{
    private const int GremlinPort = 8182;

    // TinkerGraph's default id manager only accepts numeric ids; Thor writes tenant-namespaced
    // string ids (GraphIds), so accept any id type, as Neptune does.
    private const string TinkerGraphProperties = """
        gremlin.graph=org.apache.tinkerpop.gremlin.tinkergraph.structure.TinkerGraph
        gremlin.tinkergraph.vertexIdManager=ANY
        """;

    // Replaces the image's default server config, which registers no GraphSON 2 serializer: the
    // server then answers a GraphSON 2 client in GraphBinary, which fails to parse ("'0x81' is an
    // invalid start of a value"). GraphSON 2 is what GremlinConnectionFactory speaks to Neptune.
    // GraphBinary stays listed as the image default had it. Groovy script support is kept because
    // GraphVertexStore seeds vertices with scripts.
    private const string ServerConfig = """
        host: 0.0.0.0
        port: 8182
        channelizer: org.apache.tinkerpop.gremlin.server.channel.WebSocketChannelizer
        graphs: {
          graph: conf/tinkergraph-empty.properties}
        scriptEngines: {
          gremlin-groovy: {
            plugins: { org.apache.tinkerpop.gremlin.server.jsr223.GremlinServerGremlinPlugin: {},
                       org.apache.tinkerpop.gremlin.tinkergraph.jsr223.TinkerGraphGremlinPlugin: {},
                       org.apache.tinkerpop.gremlin.jsr223.ScriptFileGremlinPlugin: {files: [scripts/empty-sample.groovy]}}}}
        serializers:
          - { className: org.apache.tinkerpop.gremlin.util.ser.GraphSONMessageSerializerV2 }
          - { className: org.apache.tinkerpop.gremlin.util.ser.GraphBinaryMessageSerializerV1 }
        """;

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("tinkerpop/gremlin-server:3.7.3")
        .WithPortBinding(GremlinPort, true)
        .WithResourceMapping(Encoding.UTF8.GetBytes(TinkerGraphProperties), "/opt/gremlin-server/conf/tinkergraph-empty.properties")
        .WithResourceMapping(Encoding.UTF8.GetBytes(ServerConfig), "/opt/gremlin-server/conf/gremlin-server.yaml")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Channel started at port"))
        .Build();

    public IGremlinClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // Same serializer GremlinConnectionFactory uses against Neptune, so result shapes match.
        Client = new GremlinClient(
            new GremlinServer(_container.Hostname, _container.GetMappedPublicPort(GremlinPort)),
            new GraphSON2MessageSerializer(new GraphSON2Reader(), new GraphSON2Writer()));
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }
}
