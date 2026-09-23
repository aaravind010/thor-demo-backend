using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AccountTypeRepository(TenantDbContext context)
    : Repository<AccountType>(context), IAccountTypeRepository
{
}
