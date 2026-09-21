using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Thor.Auth;

/// <summary>
/// Signs a compact JWT (RS256) from caller-supplied claims using an RSA private key. Generic
/// on purpose — it knows nothing about what the claims mean; that's the issuing service's
/// business logic (e.g. Thor.Api's connector-registration flow). Kept here, not in a specific
/// service, so any future consumer that issues or validates one of these tokens shares the
/// same signing path instead of re-implementing it. Asymmetric signing means a downstream
/// validator only ever needs the matching public key (see <see cref="ThorTokenValidator"/>)
/// — a compromised validator can verify tokens but never forge them (ADR §5.2).
/// </summary>
public static class JwtTokenIssuer
{
    public static string Issue(string privateKeyPem, string issuer, string audience, TimeSpan lifetime, IEnumerable<Claim> claims)
    {
        var now = DateTimeOffset.UtcNow;

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var signingCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
        {
            // A fresh RSA key is imported (and disposed) on every call, but RsaSecurityKey has no
            // KeyId set, so the default CryptoProviderFactory's signature-provider cache treats
            // every call as the same key/algorithm pair and reuses a provider bound to whichever
            // RSA instance signed first — which is disposed by the time it's called again. Each
            // call gets its own uncached factory so it never reuses another call's disposed key.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.Add(lifetime).UtcDateTime,
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
