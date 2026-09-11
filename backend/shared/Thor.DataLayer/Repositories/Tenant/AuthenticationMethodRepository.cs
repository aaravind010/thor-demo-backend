using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AuthenticationMethodRepository(TenantDbContext context)
    : Repository<AuthenticationMethod>(context), IAuthenticationMethodRepository
{
}
