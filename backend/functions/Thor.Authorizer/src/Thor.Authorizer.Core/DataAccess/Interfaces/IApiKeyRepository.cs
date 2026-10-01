namespace Thor.Authorizer.Core.DataAccess;

public interface IApiKeyRepository
{
    Task<ApiKeyRecord?> GetByKeyIdAsync(string keyId);

    Task<IReadOnlyList<ApiScope>> GetScopesForKeyAsync(string keyId);
}
