using Thor.Authorizer.Core.DataAccess;

namespace Thor.Authorizer.Core.Auth.ApiKey;

/// <summary>
/// Validates "{key_id}.{secret}" API key material. Key material format: split on the first '.'
/// only — key_id is opaque, secret is opaque and may itself contain dots.
/// Cheap checks (status, expiry) run before the PBKDF2 hash verify, since the hash compute is
/// by far the most expensive step and a revoked/expired key should never pay that cost.
/// </summary>
public sealed class ApiKeyValidator : IApiKeyValidator
{
    private const string ActiveStatus = "active";

    private readonly IApiKeyRepository _repository;
    private readonly IApiKeyHasher _hasher;
    private readonly TimeProvider _timeProvider;

    public ApiKeyValidator(IApiKeyRepository repository, IApiKeyHasher hasher, TimeProvider timeProvider)
    {
        _repository = repository;
        _hasher = hasher;
        _timeProvider = timeProvider;
    }

    public async Task<ApiKeyValidationResult> ValidateAsync(string tenantId, string keyMaterial)
    {
        var separatorIndex = keyMaterial.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == keyMaterial.Length - 1)
        {
            return ApiKeyValidationResult.Failure("malformed api key material");
        }

        var keyId = keyMaterial[..separatorIndex];
        var secret = keyMaterial[(separatorIndex + 1)..];

        // Tenant-scoped lookup ONLY — never a global key_id lookup across tenants.
        var record = await _repository.GetByKeyIdAsync(tenantId, keyId);
        if (record is null)
        {
            return ApiKeyValidationResult.Failure("key not found for tenant");
        }

        if (!string.Equals(record.StatusId, ActiveStatus, StringComparison.Ordinal))
        {
            return ApiKeyValidationResult.Failure("key not active");
        }

        if (record.ExpiresAt is { } expiresAt && expiresAt <= _timeProvider.GetUtcNow())
        {
            return ApiKeyValidationResult.Failure("key expired");
        }

        if (!await _hasher.VerifyAsync(secret, record.SecretHash))
        {
            return ApiKeyValidationResult.Failure("secret mismatch");
        }

        var scopes = await _repository.GetScopesForKeyAsync(tenantId, keyId);
        return ApiKeyValidationResult.Success(record.PrincipalId, scopes.Select(s => s.Name).ToArray());
    }
}
