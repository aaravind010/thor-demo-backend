namespace Thor.Authorizer.Core.DataAccess.Fakes;

public sealed class InMemoryApiKeyRepository : IApiKeyRepository
{
    private readonly Dictionary<string, ApiKeyRecord> _keys;
    private readonly Dictionary<string, IReadOnlyList<ApiScope>> _scopes;

    public InMemoryApiKeyRepository(
        IEnumerable<ApiKeyRecord> seedKeys,
        IReadOnlyDictionary<string, IReadOnlyList<ApiScope>>? seedScopes = null)
    {
        _keys = seedKeys.ToDictionary(k => k.KeyId);
        _scopes = seedScopes is null
            ? new Dictionary<string, IReadOnlyList<ApiScope>>()
            : new Dictionary<string, IReadOnlyList<ApiScope>>(seedScopes);
    }

    public Task<ApiKeyRecord?> GetByKeyIdAsync(string keyId) =>
        Task.FromResult(_keys.GetValueOrDefault(keyId));

    public Task<IReadOnlyList<ApiScope>> GetScopesForKeyAsync(string keyId) =>
        Task.FromResult(_scopes.GetValueOrDefault(keyId, Array.Empty<ApiScope>()));
}
