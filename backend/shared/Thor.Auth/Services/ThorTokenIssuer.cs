using System.Security.Claims;

namespace Thor.Auth;

/// <summary>
/// A freshly-issued refresh token. <see cref="Token"/> is the bearer string to hand back to the
/// caller; <see cref="Salt"/>/<see cref="SecretHash"/>/<see cref="ExpiresAt"/> are what the
/// issuing service must persist so a later <c>/register/refresh</c> call can verify it — this
/// type carries them out without the caller ever touching the raw secret or hashing itself.
/// </summary>
public readonly record struct IssuedRefreshToken(Guid TokenId, string Token, string Salt, string SecretHash, DateTimeOffset ExpiresAt);

/// <summary>
/// Owns both credentials issued by a connector registration flow (e.g. Thor.Api's
/// <c>POST /register</c> / <c>POST /register/refresh</c>): the short-lived access JWT and the
/// rotating refresh token — claim shape, credential generation, and hashing all in one place.
/// Callers pass in already-resolved identity values and signing config; nothing here touches a
/// database or a request pipeline. Persisting the refresh token and validating it once loaded
/// (revoked/expired checks) stay the caller's job, since the caller owns that DbContext.
/// </summary>
public sealed class ThorTokenIssuer : ITokenIssuer
{
    /// <summary>Prefix for refresh tokens, matching the ADR §5.2 <c>thor_{id}_{secret}</c> key-format style.</summary>
    public const string RefreshTokenPrefix = "thor_rt";

    public const string SubjectClaimType = "sub";
    public const string ScopeClaimType = "scope";
    public const string TenantIdClaimType = "tenant_id";

    public string IssueAccessToken(
        string privateKeyPem, string issuer, string audience, TimeSpan lifetime, Guid keyId, Guid tenantId, string roleType)
    {
        var claims = new[]
        {
            new Claim(SubjectClaimType, keyId.ToString()),
            new Claim(TenantIdClaimType, tenantId.ToString()),
            new Claim(ScopeClaimType, roleType),
        };

        return JwtTokenIssuer.Issue(privateKeyPem, issuer, audience, lifetime, claims);
    }

    public IssuedRefreshToken IssueRefreshToken(string pepper, TimeSpan lifetime)
    {
        var tokenId = Guid.NewGuid();
        var secret = SecretHasher.GenerateSecret();
        var salt = SecretHasher.GenerateSalt();
        var hash = SecretHasher.ComputeHash(pepper, salt, secret);
        var token = PrefixedSecretToken.Format(RefreshTokenPrefix, tokenId, secret);

        return new IssuedRefreshToken(tokenId, token, salt, hash, DateTimeOffset.UtcNow.Add(lifetime));
    }
}
