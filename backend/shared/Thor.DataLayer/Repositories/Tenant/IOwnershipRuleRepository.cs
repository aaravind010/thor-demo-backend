using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IOwnershipRuleRepository : IRepository<OwnershipRule>
{
    /// <summary>Active rules scoped to one <c>applies_to</c> value (e.g. "account").</summary>
    Task<IReadOnlyList<OwnershipRule>> GetActiveAsync(string appliesTo, CancellationToken cancellationToken = default);

    /// <summary>True when a rule already uses <paramref name="ruleName"/> (<c>rule_name</c> is unique).</summary>
    Task<bool> ExistsByNameAsync(string ruleName, CancellationToken cancellationToken = default);

    /// <summary>Keyset-paged read, optionally filtered; null filters are not applied.</summary>
    Task<KeysetPage<OwnershipRule>> ListAsync(
        string? ruleType, string? appliesTo, bool? isActive, Guid? after, int limit,
        CancellationToken cancellationToken = default);
}
