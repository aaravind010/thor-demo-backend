using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class ScanConfigRepository(TenantDbContext context)
    : Repository<ScanConfig>(context), IScanConfigRepository
{
}
