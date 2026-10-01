using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IOwnershipVoteRepository : IRepository<OwnershipVote>
{
    /// <summary>Idempotent batch insert — conflicting (EntityId, RuleId, RunId) rows are silently dropped.</summary>
    Task UpsertBatchAsync(IReadOnlyList<OwnershipVote> votes, CancellationToken cancellationToken = default);

    /// <summary>Keyset-paged read, optionally filtered; null filters are not applied.</summary>
    Task<KeysetPage<OwnershipVote>> ListAsync(
        Guid? entityId, Guid? ruleId, string? runId, Guid? after, int limit,
        CancellationToken cancellationToken = default);
}
