using Microsoft.EntityFrameworkCore;
using Thor.Api.Exceptions;
using Thor.Api.Models;
using Thor.Auth;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;
using Thor.DataLayer.Repositories;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST v{version}/connector-api-keys</c>: issues a new connector API-key credential
/// (ADR §5.2 format: <c>thor_{KeyId}_{secret}</c>) for a tenant, granting it one scope, and
/// stores only the salted HMAC hash of the secret (Thor.Auth.SecretHasher) in the Master
/// metadata DB — the raw key is returned once, in the response, and never persisted or logged.
/// </summary>
public sealed class ConnectorApiKeyService(
    IMasterDbContextFactory masterDbContextFactory,
    MasterConnectionInfo masterConnectionInfo,
    ConnectorSecurityOptions securityOptions,
    ILogger<ConnectorApiKeyService> logger)
{
    public async Task<ConnectorApiKeyResponse> CreateAsync(
        Guid tenantId, CreateConnectorApiKeyRequest request, CancellationToken cancellationToken)
    {
        using var masterDb = masterDbContextFactory.Create(masterConnectionInfo);

        var tenantRepository = new TenantRepository(masterDb);
        _ = await tenantRepository.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new TenantNotFoundException(tenantId);

        var scope = await masterDb.ApiScopes.SingleOrDefaultAsync(
            s => s.ScopeText == request.Scope, cancellationToken)
            ?? throw new ScopeNotFoundException(request.Scope);

        var salt = SecretHasher.GenerateSalt();
        var secret = SecretHasher.GenerateSecret();
        var now = DateTimeOffset.UtcNow;

        var apiKey = new TenantApiKey
        {
            KeyId = Guid.NewGuid(),
            TenantId = tenantId,
            SecretHash = SecretHasher.ComputeHash(securityOptions.SecretPepper, salt, secret),
            Salt = salt,
            Label = request.Label,
            StatusId = ApiKeyStatus.Active,
            CreatedAt = now,
            ExpiresAt = request.ExpiresInDays > 0 ? now.AddDays(request.ExpiresInDays) : null,
        };

        var keyRepository = new TenantApiKeyRepository(masterDb);
        await keyRepository.AddAsync(apiKey, cancellationToken);
        await masterDb.KeyScopeMaps.AddAsync(new KeyScopeMap { ApiKey = apiKey, Scope = scope }, cancellationToken);

        await masterDb.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created connector API key {KeyId} for tenant {TenantId} with scope {Scope}",
            apiKey.KeyId, tenantId, request.Scope);

        var rawApiKey = PrefixedSecretToken.Format(ApiKeyConstants.Prefix, apiKey.KeyId, secret);

        return new ConnectorApiKeyResponse(apiKey.KeyId, request.Scope, apiKey.Label, apiKey.CreatedAt, apiKey.ExpiresAt, rawApiKey);
    }
}
