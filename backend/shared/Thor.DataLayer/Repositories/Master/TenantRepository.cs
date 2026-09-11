using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class TenantRepository(MasterDbContext context)
    : Repository<Tenant>(context), ITenantRepository
{
    private readonly MasterDbContext _context = context;

    public Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default) =>
        _context.Tenants.FirstOrDefaultAsync(tenant => tenant.Subdomain == subdomain, cancellationToken);
}
