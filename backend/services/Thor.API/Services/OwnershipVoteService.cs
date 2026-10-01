using Thor.Api.Models;
using Thor.DataConnectionManager;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>Backs <c>GET /ownership-votes</c> and <c>GET /ownership-votes/{id}</c>: reads <see cref="OwnershipVote"/> rows from the caller's tenant database.</summary>
public sealed class OwnershipVoteService(ITenantConnectionManager tenantConnectionManager)
{
    public async Task<CursorPage<OwnershipVoteResponse>> ListAsync(
        Guid tenantId, Guid? entityId, Guid? ruleId, string? runId, Guid? after, int limit,
        CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var page = await new OwnershipVoteRepository(tenantDb).ListAsync(entityId, ruleId, runId, after, limit, cancellationToken);
        return new CursorPage<OwnershipVoteResponse>(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<OwnershipVoteResponse?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        using var tenantDb = await tenantConnectionManager.GetTenantDbContextAsync(tenantId, cancellationToken);
        var vote = await new OwnershipVoteRepository(tenantDb).GetByIdAsync(id, cancellationToken);
        return vote is null ? null : ToResponse(vote);
    }

    private static OwnershipVoteResponse ToResponse(OwnershipVote vote) => new(
        vote.Id, vote.EntityId, vote.RuleId, vote.VotedFor, vote.VoteWeight, vote.RunId, vote.CreatedAt);
}
