using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class TenantApiKeyRepository(MasterDbContext context)
    : Repository<TenantApiKey>(context), ITenantApiKeyRepository
{
    public Task<TenantApiKey?> GetWithScopesAsync(Guid keyId, CancellationToken cancellationToken = default) =>
        context.TenantApiKeys
            .Include(k => k.ScopeMaps).ThenInclude(m => m.Scope)
            .SingleOrDefaultAsync(k => k.KeyId == keyId, cancellationToken);
}
