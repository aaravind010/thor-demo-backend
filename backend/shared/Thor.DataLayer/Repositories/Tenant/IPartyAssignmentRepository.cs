using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed record PartyAssignmentUpsertResult(Guid Id, Guid EntityId, Guid IdentityId, int Rank);

public interface IPartyAssignmentRepository : IRepository<PartyAssignment>
{
    /// <summary>
    /// Atomically inserts every row not already inserted for this run (<c>ON CONFLICT (entity_type,
    /// entity_id, identity_id, run_id) DO NOTHING</c>) and returns only the rows this call actually
    /// inserted — the set a caller should emit assignment-event side effects for. Conflicting rows
    /// are silently dropped, making a retried flush over the same run id race-safe.
    /// </summary>
    Task<IReadOnlyList<PartyAssignmentUpsertResult>> UpsertBatchAsync(
        IReadOnlyList<PartyAssignment> assignments, CancellationToken cancellationToken = default);

    /// <summary>Keyset-paged read, optionally filtered; null filters are not applied.</summary>
    Task<KeysetPage<PartyAssignment>> ListAsync(
        string? entityType, Guid? entityId, Guid? identityId, string? runId, bool? isActive, Guid? after, int limit,
        CancellationToken cancellationToken = default);
}
