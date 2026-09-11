using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class TenantRoutingRepository(MasterDbContext context)
    : Repository<TenantRouting>(context), ITenantRoutingRepository
{
}
