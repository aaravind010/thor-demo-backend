using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class AuthenticationTypeRepository(MasterDbContext context)
    : Repository<AuthenticationType>(context), IAuthenticationTypeRepository
{
}
