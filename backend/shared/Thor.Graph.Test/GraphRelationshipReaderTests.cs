using Thor.Graph.Test.Fakes;

namespace Thor.Graph.Test;

/// <summary>
/// Checks what the reader sends (bytecode, tenant-scoped) and how it shapes what comes back.
/// Whether the traversals return the right vertices is covered against a real Gremlin Server in
/// <see cref="GraphRelationshipReaderIntegrationTests"/>.
/// </summary>
public sealed class GraphRelationshipReaderTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private string Vid(string label, Guid id) => $"{_tenantId:N}:{label}:{id:N}";

    private Dictionary<string, object> VertexRow(string label, Guid id, string displayName) => new()
    {
        ["vid"] = Vid(label, id),
        ["label"] = label,
        ["props"] = new Dictionary<object, object>
        {
            ["tenantId"] = _tenantId.ToString(),
            ["pgId"] = id.ToString(),
            ["displayName"] = displayName,
        },
    };

    [Fact]
    public async Task GetNeighborsAsync_SendsBytecodeScopedToTenant_NotAScript()
    {
        var client = new FakeGremlinClient();
        var reader = new GraphRelationshipReader(client);
        var accountId = Guid.NewGuid();

        await reader.GetNeighborsAsync(_tenantId, new GraphVertexRef("account", accountId), GraphDirection.Out, [], null, 10);

        var request = Assert.Single(client.Requests);
        Assert.Equal("bytecode", request.Operation);
        Assert.False(request.Arguments.ContainsKey("bindings"));

        var text = request.Text();
        Assert.StartsWith($"V({Vid("account", accountId)}).has(tenantId,{_tenantId})", text);
        // The neighbour hop is tenant-filtered too, not just the start vertex.
        Assert.Contains($"inV().has(tenantId,{_tenantId})", text);
        Assert.Contains("limit(11)", text);
    }

    [Fact]
    public async Task GetNeighborsAsync_WithCursorAndRelTypes_FiltersOnBoth()
    {
        var client = new FakeGremlinClient();
        var reader = new GraphRelationshipReader(client);
        var after = Guid.NewGuid();

        await reader.GetNeighborsAsync(
            _tenantId, new GraphVertexRef("grp", Guid.NewGuid()), GraphDirection.In, [GraphRelTypes.MemberOf], after, 10);

        var text = Assert.Single(client.Requests).Text();
        Assert.Contains("inE(MEMBER_OF).outV()", text);
        Assert.Contains($"has(pgId,gt({after}))", text);
    }

    [Fact]
    public async Task GetNeighborsAsync_EmptyPage_SkipsEdgeQuery()
    {
        var client = new FakeGremlinClient();
        var reader = new GraphRelationshipReader(client);

        var page = await reader.GetNeighborsAsync(_tenantId, new GraphVertexRef("account", Guid.NewGuid()), GraphDirection.Both, [], null, 10);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task GetNeighborsAsync_GroupsEdgesPerNeighbour_AndSetsCursorWhenMoreRowsExist()
    {
        var accountId = Guid.NewGuid();
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        var groupC = Guid.NewGuid();
        var client = new FakeGremlinClient()
            // limit is 2, so the third row only signals that another page exists.
            .Returns(VertexRow("grp", groupA, "A"), VertexRow("grp", groupB, "B"), VertexRow("grp", groupC, "C"))
            .Returns(
                new Dictionary<string, object>
                {
                    ["other"] = Vid("grp", groupA), ["relType"] = "MEMBER_OF", ["direction"] = "out",
                    ["props"] = new Dictionary<object, object> { ["tenantId"] = _tenantId.ToString() },
                },
                new Dictionary<string, object>
                {
                    ["other"] = Vid("grp", groupA), ["relType"] = "MANAGED_BY", ["direction"] = "in",
                    ["props"] = new Dictionary<object, object>(),
                });
        var reader = new GraphRelationshipReader(client);

        var page = await reader.GetNeighborsAsync(_tenantId, new GraphVertexRef("account", accountId), GraphDirection.Both, [], null, 2);

        Assert.Equal([groupA, groupB], page.Items.Select(i => i.Vertex.Id));
        Assert.Equal(groupB, page.NextCursor);

        var first = page.Items[0];
        Assert.Equal("grp", first.Vertex.EntityType);
        Assert.Equal("A", first.Vertex.Properties["displayName"]);
        Assert.False(first.Vertex.Properties.ContainsKey("tenantId"));
        Assert.False(first.Vertex.Properties.ContainsKey("pgId"));
        Assert.Equal(
            [("MEMBER_OF", GraphDirection.Out), ("MANAGED_BY", GraphDirection.In)],
            first.Edges.Select(e => (e.RelType, e.Direction)));
        Assert.Empty(first.Edges[0].Properties);
        Assert.Empty(page.Items[1].Edges);

        // The edge query is restricted to the page's neighbours only.
        var edgeQuery = client.Requests[1].Text();
        Assert.Contains($"within({Vid("grp", groupA)},{Vid("grp", groupB)})", edgeQuery);
        Assert.DoesNotContain(Vid("grp", groupC), edgeQuery);
    }

    [Fact]
    public async Task GetReachableAsync_ReportsDepthExcludingStartVertex()
    {
        var groupId = Guid.NewGuid();
        var row = VertexRow("grp", groupId, "Nested");
        row["len"] = 3L;
        var client = new FakeGremlinClient().Returns(row);
        var reader = new GraphRelationshipReader(client);

        var page = await reader.GetReachableAsync(
            _tenantId, new GraphVertexRef("account", Guid.NewGuid()), GraphRelTypes.MemberOf, GraphDirection.Out, 5, null, 10);

        var reached = Assert.Single(page.Items);
        Assert.Equal(groupId, reached.Vertex.Id);
        Assert.Equal(2, reached.Depth);

        var text = Assert.Single(client.Requests).Text();
        Assert.Contains($"repeat(out(MEMBER_OF).has(tenantId,{_tenantId}).simplePath())", text);
        Assert.Contains("times(5)", text);
    }

    [Fact]
    public async Task GetEffectiveAccessAsync_MarksDirectGrantsWithNoVia()
    {
        var accountId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var client = new FakeGremlinClient()
            .Returns(VertexRow("asset", assetId, "Safe"))
            .Returns(
                new Dictionary<string, object>
                {
                    ["asset"] = Vid("asset", assetId), ["viaVid"] = Vid("account", accountId), ["viaLabel"] = "account",
                    ["viaProps"] = new Dictionary<object, object> { ["pgId"] = accountId.ToString() },
                    ["props"] = new Dictionary<object, object> { ["is_admin"] = true },
                },
                new Dictionary<string, object>
                {
                    ["asset"] = Vid("asset", assetId), ["viaVid"] = Vid("grp", groupId), ["viaLabel"] = "grp",
                    ["viaProps"] = new Dictionary<object, object> { ["pgId"] = groupId.ToString(), ["displayName"] = "Admins" },
                    ["props"] = new Dictionary<object, object> { ["is_admin"] = false },
                });
        var reader = new GraphRelationshipReader(client);

        var page = await reader.GetEffectiveAccessAsync(_tenantId, new GraphVertexRef("account", accountId), 5, null, 10);

        var access = Assert.Single(page.Items);
        Assert.Equal(assetId, access.Asset.Id);
        Assert.Equal(2, access.Grants.Count);
        Assert.Null(access.Grants[0].Via);
        Assert.Equal(true, access.Grants[0].Properties["is_admin"]);
        Assert.Equal(groupId, access.Grants[1].Via!.Id);
        Assert.Equal("Admins", access.Grants[1].Via!.Properties["displayName"]);
    }
}
