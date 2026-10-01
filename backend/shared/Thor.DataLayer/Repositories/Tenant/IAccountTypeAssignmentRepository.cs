using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IAccountTypeAssignmentRepository : IRepository<AccountTypeAssignment>
{
    /// <summary>
    /// Atomically inserts every row not already assigned (<c>ON CONFLICT (entity_id, entity_type)
    /// DO NOTHING</c>) and returns only the entity ids this call actually inserted — the set a
    /// caller should emit assignment-event/account-stamp side effects for. Conflicting rows are
    /// silently dropped, making concurrent/retried calls over overlapping accounts race-safe.
    /// </summary>
    Task<IReadOnlyList<Guid>> UpsertBatchAsync(
        IReadOnlyList<AccountTypeAssignment> assignments, CancellationToken cancellationToken = default);
}
