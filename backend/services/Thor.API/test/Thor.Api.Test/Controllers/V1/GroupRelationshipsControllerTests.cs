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

public class GroupRelationshipsControllerTests
{
    private readonly InMemoryTenant _tenant = new();
    private readonly IGraphRelationshipReader _graph = Substitute.For<IGraphRelationshipReader>();
    private readonly Guid _groupId = Guid.NewGuid();

    public GroupRelationshipsControllerTests()
    {
        var sourceId = Guid.NewGuid();
        _tenant.Seed(TestEntities.Source(sourceId), TestEntities.Group(sourceId, _groupId));
        _graph.GetReachableAsync(default, default, default!, default, default, default, default, default)
            .ReturnsForAnyArgs(new GraphPage<GraphReachedVertex>([], null));
    }

    private GroupRelationshipsController CreateController(ITenantConnectionManager? manager = null) =>
        InMemoryTenant.WithTenantHeader(
            new GroupRelationshipsController(new GroupRelationshipService(manager ?? _tenant.ConnectionManager(), _graph)));

    [Fact]
    public async Task GetMembers_Transitive_WalksMemberOfInbound()
    {
        var memberId = Guid.NewGuid();
        _graph.GetReachableAsync(default, default, default!, default, default, default, default, default)
            .ReturnsForAnyArgs(new GraphPage<GraphReachedVertex>(
                [new GraphReachedVertex(new GraphVertexView("account", memberId, new Dictionary<string, object?>()), 2)], null));

        var result = await CreateController().GetMembers(_groupId, transitive: true, maxDepth: 4);

        var item = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<ReachableEntityResponse>>()
            .Which.Items.Should().ContainSingle().Subject;
        item.Entity.Should().BeEquivalentTo(new { EntityType = "account", Id = memberId });
        item.Depth.Should().Be(2);
        await _graph.Received(1).GetReachableAsync(
            Arg.Any<Guid>(), new GraphVertexRef("grp", _groupId), GraphRelTypes.MemberOf, GraphDirection.In,
            4, null, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetParents_Direct_WalksMemberOfOutboundOneHop()
    {
        var result = await CreateController().GetParents(_groupId);

        result.Result.Should().BeOfType<OkObjectResult>();
        await _graph.Received(1).GetReachableAsync(
            Arg.Any<Guid>(), new GraphVertexRef("grp", _groupId), GraphRelTypes.MemberOf, GraphDirection.Out,
            1, null, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRelationships_PassesCursorAndDirection()
    {
        var after = Guid.NewGuid();
        _graph.GetNeighborsAsync(default, default, default, default!, default, default, default)
            .ReturnsForAnyArgs(new GraphPage<GraphNeighbor>([], null));

        var result = await CreateController().GetRelationships(_groupId, direction: "in", after: after, limit: 10);

        result.Result.Should().BeOfType<OkObjectResult>();
        await _graph.Received(1).GetNeighborsAsync(
            Arg.Any<Guid>(), new GraphVertexRef("grp", _groupId), GraphDirection.In,
            Arg.Is<IReadOnlyCollection<string>>(r => r.Count == 0), after, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAccess_UsesGroupVertex()
    {
        _graph.GetEffectiveAccessAsync(default, default, default, default, default, default)
            .ReturnsForAnyArgs(new GraphPage<GraphAccess>([], null));

        var result = await CreateController().GetAccess(_groupId, maxDepth: 2);

        result.Result.Should().BeOfType<OkObjectResult>();
        await _graph.Received(1).GetEffectiveAccessAsync(
            Arg.Any<Guid>(), new GraphVertexRef("grp", _groupId), 2, null, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMembers_UnknownGroup_ReturnsNotFound()
    {
        var result = await CreateController().GetMembers(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetMembers_TenantNotFound_ReturnsForbidden()
    {
        var result = await CreateController(InMemoryTenant.UnknownTenantConnectionManager()).GetMembers(_groupId);

        result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
