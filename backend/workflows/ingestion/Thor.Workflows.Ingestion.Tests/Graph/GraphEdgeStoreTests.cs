using Thor.Graph;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Graph;

public sealed class GraphEdgeStoreTests
{
    [Fact]
    public async Task UpsertEdgeAsync_SubmitsUpsertScript_WithTenantNamespacedEndpointsAndRelType()
    {
        var client = new FakeGremlinClient();
        var store = new GraphEdgeStore(client);
        var tenantId = Guid.NewGuid();
        var from = new GraphVertexRef("account", Guid.NewGuid());
        var to = new GraphVertexRef("grp", Guid.NewGuid());

        await store.UpsertEdgeAsync(tenantId, "MEMBER_OF", from, to, new Dictionary<string, object?>());

        var request = Assert.Single(client.Requests);
        var script = request.Script();
        var bindings = request.Bindings();

        Assert.Contains("coalesce(inE(relType)", script);
        Assert.Contains("addE(relType).from('a').to('b')", script);

        Assert.Equal($"{tenantId:N}:account:{from.Id:N}", bindings["fromVid"]);
        Assert.Equal($"{tenantId:N}:grp:{to.Id:N}", bindings["toVid"]);
        Assert.Equal("MEMBER_OF", bindings["relType"]);
        Assert.Equal(tenantId.ToString(), bindings["tenantIdProp"]);
    }

    [Fact]
    public async Task DeleteEdgeAsync_SubmitsDropScript_WithTenantNamespacedEndpoints()
    {
        var client = new FakeGremlinClient();
        var store = new GraphEdgeStore(client);
        var tenantId = Guid.NewGuid();
        var from = new GraphVertexRef("account", Guid.NewGuid());
        var to = new GraphVertexRef("grp", Guid.NewGuid());

        await store.DeleteEdgeAsync(tenantId, "MEMBER_OF", from, to);

        var request = Assert.Single(client.Requests);
        Assert.Equal("g.V(fromVid).outE(relType).where(inV().hasId(toVid)).drop()", request.Script());
        Assert.Equal($"{tenantId:N}:account:{from.Id:N}", request.Bindings()["fromVid"]);
        Assert.Equal($"{tenantId:N}:grp:{to.Id:N}", request.Bindings()["toVid"]);
    }
}
