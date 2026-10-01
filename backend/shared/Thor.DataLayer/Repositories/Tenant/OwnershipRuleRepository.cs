using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class OwnershipRuleRepository(TenantDbContext context)
    : Repository<OwnershipRule>(context), IOwnershipRuleRepository
{
    public async Task<IReadOnlyList<OwnershipRule>> GetActiveAsync(
        string appliesTo, CancellationToken cancellationToken = default) =>
        await context.OwnershipRules
            .AsNoTracking()
            .Where(r => r.IsActive && r.AppliesTo == appliesTo)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByNameAsync(string ruleName, CancellationToken cancellationToken = default) =>
        context.OwnershipRules.AnyAsync(r => r.RuleName == ruleName, cancellationToken);

    public Task<KeysetPage<OwnershipRule>> ListAsync(
        string? ruleType, string? appliesTo, bool? isActive, Guid? after, int limit,
        CancellationToken cancellationToken = default) =>
        context.OwnershipRules
            .AsNoTracking()
            .Where(r => ruleType == null || r.RuleType == ruleType)
            .Where(r => appliesTo == null || r.AppliesTo == appliesTo)
            .Where(r => isActive == null || r.IsActive == isActive)
            .ToKeysetPageAsync(r => r.Id, after, limit, cancellationToken);
}
