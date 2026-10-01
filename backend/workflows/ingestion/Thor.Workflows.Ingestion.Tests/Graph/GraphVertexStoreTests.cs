using Thor.Graph;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Graph;

public sealed class GraphVertexStoreTests
{
    [Fact]
    public async Task UpsertVertexAsync_SubmitsUpsertScript_WithTenantNamespacedIdAndProperties()
    {
        var client = new FakeGremlinClient();
        var store = new GraphVertexStore(client);
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        await store.UpsertVertexAsync(tenantId, "account", entityId,
            new Dictionary<string, object?> { ["displayName"] = "Alice", ["isDisabled"] = false });

        var request = Assert.Single(client.Requests);
        var script = request.Script();
        var bindings = request.Bindings();

        Assert.Contains("coalesce(unfold(), addV(vlabel)", script);
        Assert.Contains(".property(single, 'displayName', p0)", script);
        Assert.Contains(".property(single, 'isDisabled', p1)", script);

        Assert.Equal($"{tenantId:N}:account:{entityId:N}", bindings["vid"]);
        Assert.Equal("account", bindings["vlabel"]);
        Assert.Equal(tenantId.ToString(), bindings["tenantIdProp"]);
        Assert.Equal(entityId.ToString(), bindings["pgIdProp"]);
        Assert.Equal("Alice", bindings["p0"]);
        Assert.Equal(false, bindings["p1"]);
    }

    [Fact]
    public async Task UpsertVerticesAsync_SubmitsOneScriptPerVertex()
    {
        var client = new FakeGremlinClient();
        var store = new GraphVertexStore(client);
        var tenantId = Guid.NewGuid();

        await store.UpsertVerticesAsync(tenantId, "grp",
        [
            (Guid.NewGuid(), new Dictionary<string, object?> { ["displayName"] = "Group A" }),
            (Guid.NewGuid(), new Dictionary<string, object?> { ["displayName"] = "Group B" }),
        ]);

        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task DeleteVertexAsync_SubmitsDropScript_WithTenantNamespacedId()
    {
        var client = new FakeGremlinClient();
        var store = new GraphVertexStore(client);
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        await store.DeleteVertexAsync(tenantId, "account", entityId);

        var request = Assert.Single(client.Requests);
        Assert.Equal("g.V(vid).drop()", request.Script());
        Assert.Equal($"{tenantId:N}:account:{entityId:N}", request.Bindings()["vid"]);
    }
}
