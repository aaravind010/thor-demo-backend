using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Thor.Auth;

/// <summary>
/// Validates access JWTs presented to Thor.TaskApi — the tokens Thor.Api's connector
/// registration flow mints via <see cref="ThorTokenIssuer"/>, signed RS256 with its
/// private key. This validator only ever holds the matching public key, so a compromised
/// TaskApi can verify tokens but never forge them (ADR §5.2). Checks signature, expiry, that
/// the audience is <see cref="TokenAudiences.TaskApi"/>, and that a scope claim is present.
/// </summary>
public sealed class ThorTokenValidator : ITokenValidator, IDisposable
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly RSA _publicKey;

    public ThorTokenValidator(string publicKeyPem)
    {
        _publicKey = RSA.Create();
        _publicKey.ImportFromPem(publicKeyPem);
    }

    public TokenValidationResult Validate(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TokenIssuers.ThorTaskApi,
            ValidateAudience = true,
            ValidAudience = TokenAudiences.TaskApi,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(_publicKey),
            // Pin the accepted algorithm explicitly: without this, a token forged with HS256
            // using the (public, non-secret) key bytes as an HMAC secret would still validate —
            // the classic RS256/HS256 algorithm-confusion attack.
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = ClockSkew,
        };

        // MapInboundClaims defaults to true, which rewrites short claim types like "sub" to
        // long XML-schema URIs — that would break lookups by ThorTokenIssuer's claim type
        // constants below, so keep the claim types exactly as issued.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(token, parameters, out _);
        }
        catch
        {
            // Fail closed: signature, expiry, audience, and algorithm failures all land here,
            // along with any other unexpected exception while parsing the token.
            return TokenValidationResult.Invalid;
        }

        var keyIdClaim = principal.FindFirst(ThorTokenIssuer.SubjectClaimType)?.Value;
        var tenantIdClaim = principal.FindFirst(ThorTokenIssuer.TenantIdClaimType)?.Value;
        var scope = principal.FindFirst(ThorTokenIssuer.ScopeClaimType)?.Value;

        if (!Guid.TryParse(keyIdClaim, out var keyId) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId) ||
            string.IsNullOrEmpty(scope))
        {
            return TokenValidationResult.Invalid;
        }

        return TokenValidationResult.Success(keyId, tenantId, scope);
    }

    public void Dispose() => _publicKey.Dispose();
}
