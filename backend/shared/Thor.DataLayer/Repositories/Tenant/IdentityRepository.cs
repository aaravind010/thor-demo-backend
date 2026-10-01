using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class IdentityRepository(TenantDbContext context)
    : Repository<IdentityRecord>(context), IIdentityRepository
{
    public Task<KeysetPage<IdentityRecord>> ListAsync(bool? isActive, Guid? after, int limit, CancellationToken cancellationToken = default) =>
        context.Identities
            .AsNoTracking()
            .Where(i => isActive == null || i.IsActive == isActive)
            .ToKeysetPageAsync(i => i.Id, after, limit, cancellationToken);
}
