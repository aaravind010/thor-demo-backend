using Thor.Auth;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;
using Thor.DataLayer.Repositories;
using Thor.Api.Constants;
using Thor.Api.Exceptions;
using Thor.Api.Models;

namespace Thor.Api.Services;

/// <summary>
/// Backs <c>POST /register</c> and <c>POST /register/refresh</c>: verifies a connector's
/// API key (or refresh token) against the Master metadata DB and issues a scoped JWT plus a
/// rotating refresh token. See ADR §5.2 for the API-key credential model.
/// </summary>
public sealed class ConnectorAuthService(
    IMasterDbContextFactory dbContextFactory,
    MasterConnectionInfo connectionInfo,
    ConnectorSecurityOptions securityOptions,
    ITokenIssuer tokenIssuer) : IAuthService
{
    public async Task<TaskAuthResponse> RegisterAsync(string rawApiKey, string roleType, CancellationToken cancellationToken)
    {
        if (!PrefixedSecretToken.TryParse(rawApiKey, ApiKeyConstants.Prefix, out var keyId, out var secret))
        {
            throw new InvalidApiKeyException();
        }

        using var db = dbContextFactory.Create(connectionInfo);
        var keyRepository = new TenantApiKeyRepository(db);

        var apiKey = await keyRepository.GetWithScopesAsync(keyId, cancellationToken);
        VerifyApiKey(apiKey, secret);

        EnsureScopeGranted(apiKey!, roleType);

        apiKey!.LastUsedAt = DateTimeOffset.UtcNow;

        var token = IssueAccessToken(apiKey.KeyId, apiKey.TenantId, roleType);
        var refreshToken = await IssueAndSaveRefreshTokenAsync(db, apiKey.KeyId, roleType, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new TaskAuthResponse(token, refreshToken, roleType);
    }

    public async Task<TaskAuthResponse> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        if (!PrefixedSecretToken.TryParse(rawRefreshToken, ThorTokenIssuer.RefreshTokenPrefix, out var tokenId, out var secret))
        {
            throw new InvalidRefreshTokenException();
        }

        using var db = dbContextFactory.Create(connectionInfo);
        var refreshTokenRepository = new TaskApiRefreshTokenRepository(db);

        var existingToken = await refreshTokenRepository.GetByIdAsync(tokenId, cancellationToken);
        VerifyRefreshToken(existingToken, secret);

        var keyRepository = new TenantApiKeyRepository(db);
        var apiKey = await keyRepository.GetWithScopesAsync(existingToken!.KeyId, cancellationToken);
        VerifyApiKeyIsUsable(apiKey);

        EnsureScopeGranted(apiKey!, existingToken.RoleType);

        // Single-use rotation: this token is spent the moment it is redeemed.
        existingToken.RevokedAt = DateTimeOffset.UtcNow;
        apiKey!.LastUsedAt = DateTimeOffset.UtcNow;

        var token = IssueAccessToken(apiKey.KeyId, apiKey.TenantId, existingToken.RoleType);
        var newRefreshToken = await IssueAndSaveRefreshTokenAsync(db, apiKey.KeyId, existingToken.RoleType, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new TaskAuthResponse(token, newRefreshToken, existingToken.RoleType);
    }

    private void VerifyApiKey(TenantApiKey? apiKey, string secret)
    {
        VerifyApiKeyIsUsable(apiKey);

        if (!SecretHasher.Verify(securityOptions.SecretPepper, apiKey!.Salt, secret, apiKey.SecretHash))
        {
            throw new InvalidApiKeyException();
        }
    }

    private static void VerifyApiKeyIsUsable(TenantApiKey? apiKey)
    {
        if (apiKey is null ||
            apiKey.StatusId != ApiKeyStatus.Active ||
            apiKey.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidApiKeyException();
        }
    }

    private void VerifyRefreshToken(TaskApiRefreshToken? refreshToken, string secret)
    {
        if (refreshToken is null ||
            refreshToken.RevokedAt is not null ||
            refreshToken.ExpiresAt <= DateTimeOffset.UtcNow ||
            !SecretHasher.Verify(securityOptions.SecretPepper, refreshToken.Salt, secret, refreshToken.TokenHash))
        {
            throw new InvalidRefreshTokenException();
        }
    }

    private static void EnsureScopeGranted(TenantApiKey apiKey, string roleType)
    {
        var granted = apiKey.ScopeMaps.Any(m => string.Equals(m.Scope.ScopeText, roleType, StringComparison.OrdinalIgnoreCase));
        if (!granted)
        {
            throw new ScopeNotGrantedException(roleType);
        }
    }

    private string IssueAccessToken(Guid keyId, Guid tenantId, string roleType) =>
        tokenIssuer.IssueAccessToken(
            securityOptions.JwtPrivateKeyPem, AuthConstants.JwtIssuer, AuthConstants.TaskApiAudience, AuthConstants.AccessTokenLifetime, keyId, tenantId, roleType);

    private async Task<string> IssueAndSaveRefreshTokenAsync(MasterDbContext db, Guid keyId, string roleType, CancellationToken cancellationToken)
    {
        var issued = tokenIssuer.IssueRefreshToken(securityOptions.SecretPepper, AuthConstants.RefreshTokenLifetime);

        await db.TaskApiRefreshTokens.AddAsync(new TaskApiRefreshToken
        {
            TokenId = issued.TokenId,
            KeyId = keyId,
            Salt = issued.Salt,
            TokenHash = issued.SecretHash,
            RoleType = roleType,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = issued.ExpiresAt,
        }, cancellationToken);

        return issued.Token;
    }
}
