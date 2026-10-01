using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AccountTypeRuleRepository(TenantDbContext context)
    : Repository<AccountTypeRule>(context), IAccountTypeRuleRepository
{
    public async Task<IReadOnlyList<AccountTypeRule>> GetActiveAsync(
        string appliesTo, CancellationToken cancellationToken = default) =>
        await context.AccountTypeRules
            .AsNoTracking()
            .Where(r => r.IsActive && r.AppliesTo == appliesTo)
            .ToListAsync(cancellationToken);

    public Task<KeysetPage<AccountTypeRule>> ListAsync(
        string? appliesTo, bool? isActive, Guid? targetAccountTypeId, Guid? after, int limit,
        CancellationToken cancellationToken = default) =>
        context.AccountTypeRules
            .AsNoTracking()
            .Where(r => appliesTo == null || r.AppliesTo == appliesTo)
            .Where(r => isActive == null || r.IsActive == isActive)
            .Where(r => targetAccountTypeId == null || r.TargetAccountTypeId == targetAccountTypeId)
            .ToKeysetPageAsync(r => r.Id, after, limit, cancellationToken);
}
