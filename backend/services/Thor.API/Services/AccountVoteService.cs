using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>Backs <c>GET /account-votes</c> and <c>GET /account-votes/{id}</c>: reads ATRE's <see cref="AtreVote"/> rows from the caller's tenant database.</summary>
public sealed class AccountVoteService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<CursorPage<AccountVoteResponse>> ListAsync(
        Guid tenantId, Guid? entityId, Guid? ruleId, string? runId, Guid? after, int limit,
        CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new AtreVoteRepository(tenantDb).ListAsync(entityId, ruleId, runId, after, limit, cancellationToken);
        return new CursorPage<AccountVoteResponse>(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<AccountVoteResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var vote = await new AtreVoteRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return vote is null ? null : ToResponse(vote);
    }

    private static AccountVoteResponse ToResponse(AtreVote vote) => new(
        vote.Id, vote.EntityId, vote.RuleId, vote.VotedFor, vote.VoteWeight, vote.RunId, vote.CreatedAt);
}
