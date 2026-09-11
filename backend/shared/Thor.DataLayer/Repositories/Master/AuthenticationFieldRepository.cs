using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class AuthenticationFieldRepository(MasterDbContext context)
    : Repository<AuthenticationField>(context), IAuthenticationFieldRepository
{
}
