using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IAccountTypeRuleRepository : IRepository<AccountTypeRule>
{
    /// <summary>Active rules scoped to one <c>applies_to</c> value (e.g. "account").</summary>
    Task<IReadOnlyList<AccountTypeRule>> GetActiveAsync(string appliesTo, CancellationToken cancellationToken = default);

    /// <summary>Keyset-paged read, optionally filtered; null filters are not applied.</summary>
    Task<KeysetPage<AccountTypeRule>> ListAsync(
        string? appliesTo, bool? isActive, Guid? targetAccountTypeId, Guid? after, int limit,
        CancellationToken cancellationToken = default);
}
