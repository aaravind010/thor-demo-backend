using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.Support;
using Thor.DataConnectionManager;

namespace Thor.Api.Test.Controllers.V1;

public class PartyAssignmentsControllerTests
{
    private readonly InMemoryTenant _tenant = new();
    private readonly Guid _identityId = Guid.NewGuid();

    public PartyAssignmentsControllerTests() => _tenant.Seed(TestEntities.Identity(_identityId));

    private PartyAssignmentsController CreateController(ITenantConnectionManager? manager = null) =>
        InMemoryTenant.WithTenantHeader(
            new PartyAssignmentsController(new PartyAssignmentService(manager ?? _tenant.ConnectionManager())));

    [Fact]
    public async Task List_FiltersByEntityAndRun()
    {
        var entityId = Guid.NewGuid();
        var match = TestEntities.PartyAssignment(entityId, _identityId, runId: "run-2");
        _tenant.Seed(
            match,
            TestEntities.PartyAssignment(entityId, _identityId, runId: "run-1"),
            TestEntities.PartyAssignment(Guid.NewGuid(), _identityId, runId: "run-2"));

        var result = await CreateController().List(
            entityType: "account", entityId: entityId, identityId: null, runId: "run-2", isActive: null, after: null);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<PartyAssignmentResponse>>()
            .Which.Items.Select(a => a.Id).Should().Equal(match.Id);
    }

    [Fact]
    public async Task List_TenantNotFound_ReturnsForbidden()
    {
        var result = await CreateController(InMemoryTenant.UnknownTenantConnectionManager())
            .List(null, null, null, null, null, null);

        result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Get_Existing_ReturnsAssignment()
    {
        var assignment = TestEntities.PartyAssignment(Guid.NewGuid(), _identityId);
        _tenant.Seed(assignment);

        var result = await CreateController().Get(assignment.Id, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<PartyAssignmentResponse>()
            .Which.IdentityId.Should().Be(_identityId);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        var result = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
