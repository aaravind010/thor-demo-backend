namespace Thor.Authorizer.Core.DataAccess;

public interface IApiKeyRepository
{
    Task<ApiKeyRecord?> GetByKeyIdAsync(string tenantId, string keyId);

    Task<IReadOnlyList<ApiScope>> GetScopesForKeyAsync(string tenantId, string keyId);
}
