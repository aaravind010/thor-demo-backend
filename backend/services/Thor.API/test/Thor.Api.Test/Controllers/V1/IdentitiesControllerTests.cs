using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.Support;
using Thor.DataConnectionManager;

namespace Thor.Api.Test.Controllers.V1;

public class IdentitiesControllerTests
{
    private readonly InMemoryTenant _tenant = new();

    private IdentitiesController CreateController(ITenantConnectionManager? manager = null) =>
        InMemoryTenant.WithTenantHeader(new IdentitiesController(new IdentityService(manager ?? _tenant.ConnectionManager())));

    [Fact]
    public async Task List_FiltersByIsActive()
    {
        var active = TestEntities.Identity();
        _tenant.Seed(active, TestEntities.Identity(isActive: false));

        var result = await CreateController().List(isActive: true, after: null);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<IdentityResponse>>()
            .Which.Items.Select(i => i.Id).Should().Equal(active.Id);
    }

    [Fact]
    public async Task List_TenantNotFound_ReturnsForbidden()
    {
        var result = await CreateController(InMemoryTenant.UnknownTenantConnectionManager()).List(null, null);

        result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Get_Existing_ReturnsIdentity()
    {
        var identity = TestEntities.Identity();
        _tenant.Seed(identity);

        var result = await CreateController().Get(identity.Id, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<IdentityResponse>()
            .Which.Email.Should().Be(identity.Email);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        var result = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
