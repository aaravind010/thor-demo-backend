using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class ScanSourceMappingRepository(TenantDbContext context)
    : Repository<ScanSourceMapping>(context), IScanSourceMappingRepository
{
}
