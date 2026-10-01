using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IAccountRepository : IRepository<Account>
{
    Task<IReadOnlyList<Account>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batched <c>account.account_type_id</c> stamp for every (account id -&gt; new type id) pair
    /// in one round trip — used by ATRE after an assignment upsert to stamp only the accounts it
    /// itself won the insert race for.
    /// </summary>
    Task BulkUpdateAccountTypeAsync(
        IReadOnlyDictionary<Guid, Guid> accountTypeIdByAccountId, CancellationToken cancellationToken = default);

    /// <summary>Keyset-paged read, optionally filtered; null filters are not applied.</summary>
    Task<KeysetPage<Account>> ListAsync(
        Guid? sourceId, Guid? accountTypeId, bool? isDeleted, Guid? after, int limit,
        CancellationToken cancellationToken = default);
}
