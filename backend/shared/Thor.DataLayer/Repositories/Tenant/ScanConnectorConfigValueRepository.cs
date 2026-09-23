using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class ScanConnectorConfigValueRepository(TenantDbContext context)
    : Repository<ScanConnectorConfigValue>(context), IScanConnectorConfigValueRepository
{
}
