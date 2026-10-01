using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IAtreVoteRepository : IRepository<AtreVote>
{
    /// <summary>
    /// Atomically inserts every row not already present for its (entity_id, rule_id, run_id)
    /// (<c>ON CONFLICT DO NOTHING</c>) — makes re-running the same deterministic RunId a no-op
    /// instead of duplicating vote history.
    /// </summary>
    Task UpsertBatchAsync(IReadOnlyList<AtreVote> votes, CancellationToken cancellationToken = default);

    /// <summary>Keyset-paged read, optionally filtered; null filters are not applied.</summary>
    Task<KeysetPage<AtreVote>> ListAsync(
        Guid? entityId, Guid? ruleId, string? runId, Guid? after, int limit,
        CancellationToken cancellationToken = default);
}
