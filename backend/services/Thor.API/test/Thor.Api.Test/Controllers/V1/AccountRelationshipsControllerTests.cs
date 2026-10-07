using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.Support;
using Thor.DataConnectionManager;
using Thor.Graph;

namespace Thor.Api.Test.Controllers.V1;

public class AccountRelationshipsControllerTests
{
    private readonly InMemoryTenant _tenant = new();
    private readonly IGraphRelationshipReader _graph = Substitute.For<IGraphRelationshipReader>();
    private readonly Guid _accountId = Guid.NewGuid();

    public AccountRelationshipsControllerTests()
    {
        var sourceId = Guid.NewGuid();
        var accountType = TestEntities.AccountType();
        _tenant.Seed(TestEntities.Source(sourceId), accountType, TestEntities.Account(sourceId, accountType.Id, _accountId));
    }

    private AccountRelationshipsController CreateController(ITenantConnectionManager? manager = null, bool includeTenantHeader = true) =>
        InMemoryTenant.WithTenantHeader(
            new AccountRelationshipsController(new AccountRelationshipService(manager ?? _tenant.ConnectionManager(), _graph)),
            includeTenantHeader);

    private static GraphVertexView Vertex(string label, Guid id, string displayName) =>
        new(label, id, new Dictionary<string, object?> { ["displayName"] = displayName, ["nativeId"] = $"native-{displayName}" });

    [Fact]
    public async Task GetRelationships_MapsNeighboursAndEdges()
    {
        var groupId = Guid.NewGuid();
        var cursor = Guid.NewGuid();
        _graph.GetNeighborsAsync(Arg.Any<Guid>(), Arg.Any<GraphVertexRef>(), Arg.Any<GraphDirection>(),
                Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new GraphPage<GraphNeighbor>(
                [new GraphNeighbor(Vertex("grp", groupId, "Admins"), [new GraphEdgeView("MEMBER_OF", GraphDirection.Out, new Dictionary<string, object?>())])],
                cursor));

        var result = await CreateController().GetRelationships(_accountId, direction: "OUT", relTypes: ["member_of"]);

        var page = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<RelationshipResponse>>().Subject;
        page.NextCursor.Should().Be(cursor);
        var item = page.Items.Should().ContainSingle().Subject;
        item.Entity.EntityType.Should().Be("group");
        item.Entity.Id.Should().Be(groupId);
        item.Entity.DisplayName.Should().Be("Admins");
        item.Entity.NativeId.Should().Be("native-Admins");
        item.Edges.Should().ContainSingle().Which.Should().BeEquivalentTo(new { RelType = "MEMBER_OF", Direction = "out" });

        // Normalized input reaches the reader, addressed at this account's vertex.
        await _graph.Received(1).GetNeighborsAsync(
            Arg.Any<Guid>(), new GraphVertexRef("account", _accountId), GraphDirection.Out,
            Arg.Is<IReadOnlyCollection<string>>(r => r.SequenceEqual(new[] { "MEMBER_OF" })),
            null, 50, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("sideways", null, 50)]
    [InlineData("both", "NOT_A_LABEL", 50)]
    [InlineData("both", null, 0)]
    public async Task GetRelationships_InvalidQuery_ReturnsBadRequest(string direction, string? relType, int limit)
    {
        var result = await CreateController().GetRelationships(
            _accountId, direction, relType is null ? null : [relType], limit: limit);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _graph.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelationships_MissingTenantHeader_ReturnsBadRequest()
    {
        var result = await CreateController(includeTenantHeader: false).GetRelationships(_accountId);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetRelationships_UnknownAccount_ReturnsNotFound_WithoutQueryingGraph()
    {
        var result = await CreateController().GetRelationships(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
        _graph.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelationships_TenantNotFound_ReturnsForbidden()
    {
        var result = await CreateController(InMemoryTenant.UnknownTenantConnectionManager()).GetRelationships(_accountId);

        result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        _graph.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, null, 1)]
    [InlineData(true, null, 5)]
    [InlineData(true, 3, 3)]
    public async Task GetGroups_ResolvesDepthFromTransitiveAndMaxDepth(bool transitive, int? maxDepth, int expectedDepth)
    {
        _graph.GetReachableAsync(default, default, default!, default, default, default, default, default)
            .ReturnsForAnyArgs(new GraphPage<GraphReachedVertex>([], null));

        var result = await CreateController().GetGroups(_accountId, transitive, maxDepth);

        result.Result.Should().BeOfType<OkObjectResult>();
        await _graph.Received(1).GetReachableAsync(
            Arg.Any<Guid>(), new GraphVertexRef("account", _accountId), GraphRelTypes.MemberOf, GraphDirection.Out,
            expectedDepth, null, 50, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task GetGroups_MaxDepthOutOfRange_ReturnsBadRequest(int maxDepth)
    {
        var result = await CreateController().GetGroups(_accountId, transitive: true, maxDepth: maxDepth);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetAccess_MapsDirectAndGroupGrants()
    {
        var assetId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        _graph.GetEffectiveAccessAsync(default, default, default, default, default, default)
            .ReturnsForAnyArgs(new GraphPage<GraphAccess>(
                [new GraphAccess(Vertex("asset", assetId, "Safe"),
                [
                    new GraphAccessGrant(null, new Dictionary<string, object?> { ["is_admin"] = true }),
                    new GraphAccessGrant(Vertex("grp", groupId, "Vault Admins"), new Dictionary<string, object?>()),
                ])],
                null));

        var result = await CreateController().GetAccess(_accountId);

        var item = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<AccessResponse>>()
            .Which.Items.Should().ContainSingle().Subject;
        item.Asset.Id.Should().Be(assetId);
        item.Grants.Should().HaveCount(2);
        item.Grants[0].Via.Should().BeNull();
        item.Grants[0].Permissions["is_admin"].Should().Be(true);
        item.Grants[1].Via!.EntityType.Should().Be("group");
        item.Grants[1].Via!.Id.Should().Be(groupId);
        await _graph.Received(1).GetEffectiveAccessAsync(
            Arg.Any<Guid>(), new GraphVertexRef("account", _accountId), 5, null, 50, Arg.Any<CancellationToken>());
    }
}
