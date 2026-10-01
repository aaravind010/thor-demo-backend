using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.Support;
using Thor.DataConnectionManager;

namespace Thor.Api.Test.Controllers.V1;

public class AccountVotesControllerTests
{
    private readonly InMemoryTenant _tenant = new();

    private AccountVotesController CreateController(ITenantConnectionManager? manager = null) =>
        InMemoryTenant.WithTenantHeader(new AccountVotesController(new AccountVoteService(manager ?? _tenant.ConnectionManager())));

    [Fact]
    public async Task List_FiltersByEntityRuleAndRun()
    {
        var entityId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var match = TestEntities.AccountVote(entityId, ruleId, Guid.NewGuid(), runId: "run-2");
        _tenant.Seed(
            match,
            TestEntities.AccountVote(entityId, ruleId, Guid.NewGuid(), runId: "run-1"),
            TestEntities.AccountVote(entityId, Guid.NewGuid(), Guid.NewGuid(), runId: "run-2"));

        var result = await CreateController().List(entityId: entityId, ruleId: ruleId, runId: "run-2", after: null);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<AccountVoteResponse>>()
            .Which.Items.Select(v => v.Id).Should().Equal(match.Id);
    }

    [Fact]
    public async Task List_TenantNotFound_ReturnsForbidden()
    {
        var result = await CreateController(InMemoryTenant.UnknownTenantConnectionManager()).List(null, null, null, null);

        result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Get_Existing_ReturnsVote()
    {
        var vote = TestEntities.AccountVote(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _tenant.Seed(vote);

        var result = await CreateController().Get(vote.Id, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<AccountVoteResponse>()
            .Which.VotedFor.Should().Be(vote.VotedFor);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        var result = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
