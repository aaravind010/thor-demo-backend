using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class TenantRepository(MasterDbContext context)
    : Repository<Tenant>(context), ITenantRepository
{
}
