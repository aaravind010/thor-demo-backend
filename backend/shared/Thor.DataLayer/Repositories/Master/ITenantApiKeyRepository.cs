using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public interface ITenantApiKeyRepository : IRepository<TenantApiKey>
{
    /// <summary>Loads a key together with its granted scopes, for credential + scope checks.</summary>
    Task<TenantApiKey?> GetWithScopesAsync(Guid keyId, CancellationToken cancellationToken = default);
}
