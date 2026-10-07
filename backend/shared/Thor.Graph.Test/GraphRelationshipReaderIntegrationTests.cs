using Gremlin.Net.Driver.Remote;
using Gremlin.Net.Process.Traversal;
using Thor.Graph.Test.Fixtures;
using static Gremlin.Net.Process.Traversal.AnonymousTraversalSource;

namespace Thor.Graph.Test;

/// <summary>
/// Runs the reader's traversals against a real Gremlin Server (Docker required). Vertices are
/// seeded through the production <see cref="GraphVertexStore"/>, so they carry exactly the id scheme
/// and tenantId/pgId properties ingestion writes.
///
/// <code>
///   alice -MEMBER_OF-> g1 -MEMBER_OF-> g2 -MEMBER_OF-> g3
///   alice -MEMBER_OF-> g2                  (so g2 is depth 1, not 2)
///   alice -REPORTS_TO-> bob
///   alice -HAS_ACCESS-> safe1,  g2 -HAS_ACCESS-> safe1,  g3 -HAS_ACCESS-> safe2
///   alice -MEMBER_OF-> other   (other belongs to a different tenant — must never be returned)
/// </code>
/// </summary>
public sealed class GraphRelationshipReaderIntegrationTests(GremlinServerFixture fixture)
    : IClassFixture<GremlinServerFixture>, IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly Guid _g1 = Guid.NewGuid();
    private readonly Guid _g2 = Guid.NewGuid();
    private readonly Guid _g3 = Guid.NewGuid();
    private readonly Guid _safe1 = Guid.NewGuid();
    private readonly Guid _safe2 = Guid.NewGuid();
    private readonly Guid _otherTenantGroup = Guid.NewGuid();

    private GraphRelationshipReader Reader => new(fixture.Client);

    private GraphVertexRef Alice => new("account", _alice);

    public async Task InitializeAsync()
    {
        var vertices = new GraphVertexStore(fixture.Client);

        await vertices.UpsertVerticesAsync(_tenantId, "account",
            [(_alice, Props("Alice")), (_bob, Props("Bob"))]);
        await vertices.UpsertVerticesAsync(_tenantId, "grp",
            [(_g1, Props("G1")), (_g2, Props("G2")), (_g3, Props("G3"))]);
        await vertices.UpsertVerticesAsync(_tenantId, "asset",
            [(_safe1, Props("Safe1")), (_safe2, Props("Safe2"))]);
        await vertices.UpsertVertexAsync(_otherTenantId, "grp", _otherTenantGroup, Props("Other"));

        await AddEdgeAsync(GraphRelTypes.MemberOf, Vid(_tenantId, Alice), Vid(_tenantId, Grp(_g1)));
        await AddEdgeAsync(GraphRelTypes.MemberOf, Vid(_tenantId, Grp(_g1)), Vid(_tenantId, Grp(_g2)));
        await AddEdgeAsync(GraphRelTypes.MemberOf, Vid(_tenantId, Grp(_g2)), Vid(_tenantId, Grp(_g3)));
        await AddEdgeAsync(GraphRelTypes.MemberOf, Vid(_tenantId, Alice), Vid(_tenantId, Grp(_g2)));
        await AddEdgeAsync(GraphRelTypes.ReportsTo, Vid(_tenantId, Alice), Vid(_tenantId, new GraphVertexRef("account", _bob)));
        await AddEdgeAsync(GraphRelTypes.HasAccess, Vid(_tenantId, Alice), Vid(_tenantId, Asset(_safe1)), isAdmin: false);
        await AddEdgeAsync(GraphRelTypes.HasAccess, Vid(_tenantId, Grp(_g2)), Vid(_tenantId, Asset(_safe1)), isAdmin: true);
        await AddEdgeAsync(GraphRelTypes.HasAccess, Vid(_tenantId, Grp(_g3)), Vid(_tenantId, Asset(_safe2)));

        // Ingestion never writes a cross-tenant edge, so plant one: the reader's per-hop tenant
        // filter is what has to stop it.
        await AddEdgeAsync(GraphRelTypes.MemberOf, Vid(_tenantId, Alice), Vid(_otherTenantId, Grp(_otherTenantGroup)));
    }

    // Edges are added with bytecode rather than GraphEdgeStore: its script sets edge properties with
    // property(single, ...), and TinkerGraph only accepts a cardinality on vertex properties. Ingestion
    // writes edges through the Neptune bulk loader anyway, which carries tenantId plus the edge props
    // as plain properties, the shape built here.
    private Task AddEdgeAsync(string relType, string fromVid, string toVid, bool? isAdmin = null)
    {
        var g = Traversal().With(new DriverRemoteConnection(fixture.Client, "g"));
        var edge = g.V(fromVid).AddE(relType).To(__.V(toVid)).Property("tenantId", _tenantId.ToString());
        if (isAdmin is { } admin)
        {
            edge = edge.Property("is_admin", admin);
        }

        return edge.Promise(t => t.Iterate());
    }

    private static string Vid(Guid tenantId, GraphVertexRef vertex) => $"{tenantId:N}:{vertex.EntityType}:{vertex.Id:N}";

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetNeighborsAsync_ReturnsDirectNeighbours_WithoutCrossingTenants()
    {
        var page = await Reader.GetNeighborsAsync(_tenantId, Alice, GraphDirection.Out, [], null, 50);

        Assert.Equal(
            new HashSet<Guid> { _g1, _g2, _bob, _safe1 },
            page.Items.Select(i => i.Vertex.Id).ToHashSet());
        var safe = page.Items.Single(i => i.Vertex.Id == _safe1);
        var edge = Assert.Single(safe.Edges);
        Assert.Equal((GraphRelTypes.HasAccess, GraphDirection.Out), (edge.RelType, edge.Direction));
        Assert.Equal(false, edge.Properties["is_admin"]);
        Assert.Equal("Safe1", safe.Vertex.Properties["displayName"]);
    }

    [Fact]
    public async Task GetNeighborsAsync_FiltersByRelTypeAndDirection()
    {
        var page = await Reader.GetNeighborsAsync(_tenantId, Grp(_g2), GraphDirection.In, [GraphRelTypes.MemberOf], null, 50);

        Assert.Equal(new HashSet<Guid> { _alice, _g1 }, page.Items.Select(i => i.Vertex.Id).ToHashSet());
        Assert.All(page.Items, i => Assert.Equal(GraphDirection.In, Assert.Single(i.Edges).Direction));
    }

    [Fact]
    public async Task GetReachableAsync_ReturnsNestedGroupsAtShortestDepth()
    {
        var page = await Reader.GetReachableAsync(_tenantId, Alice, GraphRelTypes.MemberOf, GraphDirection.Out, 5, null, 50);

        Assert.Equal(
            new Dictionary<Guid, int> { [_g1] = 1, [_g2] = 1, [_g3] = 2 },
            page.Items.ToDictionary(r => r.Vertex.Id, r => r.Depth));
    }

    [Fact]
    public async Task GetReachableAsync_StopsAtMaxDepth()
    {
        var page = await Reader.GetReachableAsync(_tenantId, Grp(_g3), GraphRelTypes.MemberOf, GraphDirection.In, 1, null, 50);

        Assert.Equal([_g2], page.Items.Select(r => r.Vertex.Id));
    }

    [Fact]
    public async Task GetReachableAsync_PagesThroughEveryResultOnce()
    {
        var seen = new List<Guid>();
        Guid? cursor = null;
        do
        {
            var page = await Reader.GetReachableAsync(_tenantId, Grp(_g3), GraphRelTypes.MemberOf, GraphDirection.In, 5, cursor, 1);
            seen.AddRange(page.Items.Select(r => r.Vertex.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(new HashSet<Guid> { _g2, _g1, _alice }, seen.ToHashSet());
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public async Task GetEffectiveAccessAsync_CombinesDirectAndGroupGrants()
    {
        var page = await Reader.GetEffectiveAccessAsync(_tenantId, Alice, 5, null, 50);

        Assert.Equal(new HashSet<Guid> { _safe1, _safe2 }, page.Items.Select(a => a.Asset.Id).ToHashSet());

        var safe1 = page.Items.Single(a => a.Asset.Id == _safe1);
        Assert.Equal(2, safe1.Grants.Count);
        Assert.Contains(safe1.Grants, g => g.Via is null && Equals(g.Properties["is_admin"], false));
        Assert.Contains(safe1.Grants, g => g.Via?.Id == _g2 && Equals(g.Properties["is_admin"], true));

        var safe2 = page.Items.Single(a => a.Asset.Id == _safe2);
        Assert.Equal(_g3, Assert.Single(safe2.Grants).Via!.Id);
    }

    [Fact]
    public async Task Reads_FromAnotherTenant_SeeNothing()
    {
        var page = await Reader.GetNeighborsAsync(_otherTenantId, Alice, GraphDirection.Both, [], null, 50);

        Assert.Empty(page.Items);
    }

    private static Dictionary<string, object?> Props(string displayName) => new() { ["displayName"] = displayName };

    private static GraphVertexRef Grp(Guid id) => new("grp", id);

    private static GraphVertexRef Asset(Guid id) => new("asset", id);
}
