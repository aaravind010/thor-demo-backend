using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class SourceRepository(TenantDbContext context)
    : Repository<Source>(context), ISourceRepository
{
}
