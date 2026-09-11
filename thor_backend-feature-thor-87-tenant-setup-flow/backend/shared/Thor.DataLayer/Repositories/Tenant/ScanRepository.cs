using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class ScanRepository(TenantDbContext context)
    : Repository<Scan>(context), IScanRepository
{
}
