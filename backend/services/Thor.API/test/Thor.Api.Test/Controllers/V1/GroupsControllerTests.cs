using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.Support;
using Thor.DataConnectionManager;

namespace Thor.Api.Test.Controllers.V1;

public class GroupsControllerTests
{
    private readonly InMemoryTenant _tenant = new();
    private readonly Guid _sourceId = Guid.NewGuid();

    public GroupsControllerTests() => _tenant.Seed(TestEntities.Source(_sourceId));

    private GroupsController CreateController(ITenantConnectionManager? manager = null) =>
        InMemoryTenant.WithTenantHeader(new GroupsController(new GroupService(manager ?? _tenant.ConnectionManager())));

    [Fact]
    public async Task List_FiltersBySource_AndOmitsRawAttributes()
    {
        var otherSourceId = Guid.NewGuid();
        var group = TestEntities.Group(_sourceId);
        _tenant.Seed(TestEntities.Source(otherSourceId), group, TestEntities.Group(otherSourceId));

        var result = await CreateController().List(sourceId: _sourceId, after: null);

        var item = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<GroupResponse>>()
            .Which.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(group.Id);
        item.RawAttributes.Should().BeNull();
    }

    [Fact]
    public async Task List_TenantNotFound_ReturnsForbidden()
    {
        var result = await CreateController(InMemoryTenant.UnknownTenantConnectionManager()).List(null, null);

        result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Get_Existing_ReturnsGroupWithRawAttributes()
    {
        var group = TestEntities.Group(_sourceId);
        _tenant.Seed(group);

        var result = await CreateController().Get(group.Id, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<GroupResponse>()
            .Which.RawAttributes.Should().Be(group.RawAttributes);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        var result = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
