using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AccountTypeRepository(TenantDbContext context)
    : Repository<AccountType>(context), IAccountTypeRepository
{
    public Task<KeysetPage<AccountType>> ListAsync(Guid? after, int limit, CancellationToken cancellationToken = default) =>
        context.AccountTypes
            .AsNoTracking()
            .ToKeysetPageAsync(t => t.Id, after, limit, cancellationToken);
}
