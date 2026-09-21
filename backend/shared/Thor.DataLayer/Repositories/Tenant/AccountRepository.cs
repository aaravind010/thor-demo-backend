using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AccountRepository(TenantDbContext context)
    : Repository<Account>(context), IAccountRepository
{
    public async Task<IReadOnlyList<Account>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await context.Accounts.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
}
