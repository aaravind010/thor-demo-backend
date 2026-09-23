namespace Thor.Authorizer.Core.DataAccess.Fakes;

public sealed class InMemoryApiKeyRepository : IApiKeyRepository
{
    private readonly Dictionary<(string TenantId, string KeyId), ApiKeyRecord> _keys;
    private readonly Dictionary<(string TenantId, string KeyId), IReadOnlyList<ApiScope>> _scopes;

    public InMemoryApiKeyRepository(
        IEnumerable<ApiKeyRecord> seedKeys,
        IReadOnlyDictionary<(string TenantId, string KeyId), IReadOnlyList<ApiScope>>? seedScopes = null)
    {
        _keys = seedKeys.ToDictionary(k => (k.TenantId, k.KeyId));
        _scopes = seedScopes is null
            ? new Dictionary<(string, string), IReadOnlyList<ApiScope>>()
            : new Dictionary<(string, string), IReadOnlyList<ApiScope>>(seedScopes);
    }

    public Task<ApiKeyRecord?> GetByKeyIdAsync(string tenantId, string keyId) =>
        Task.FromResult(_keys.GetValueOrDefault((tenantId, keyId)));

    public Task<IReadOnlyList<ApiScope>> GetScopesForKeyAsync(string tenantId, string keyId) =>
        Task.FromResult(_scopes.GetValueOrDefault((tenantId, keyId), Array.Empty<ApiScope>()));
}
