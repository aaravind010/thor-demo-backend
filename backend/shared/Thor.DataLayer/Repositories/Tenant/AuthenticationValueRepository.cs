using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AuthenticationValueRepository(TenantDbContext context)
    : Repository<AuthenticationValue>(context), IAuthenticationValueRepository
{
}
