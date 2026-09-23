namespace Thor.Auth;

/// <summary>
/// Issues the access/refresh token pair for a caller-supplied identity. One implementation per
/// token-issuing flow (e.g. <see cref="ThorTokenIssuer"/> for Thor.Api's connector
/// registration flow) — the shared contract is what lets a future flow for another downstream
/// service (TaskApi today, others later) plug in without callers depending on a concrete type.
/// </summary>
public interface ITokenIssuer
{
    /// <param name="privateKeyPem">
    /// PEM-encoded RSA private key used to sign the access token (RS256). Only the issuing
    /// service holds this; validators hold the matching public key only (ADR §5.2).
    /// </param>
    /// <param name="audience">
    /// The service this token is minted for (see ADR §5.2) — lets a validator reject a token
    /// that's valid but was issued for a different downstream service.
    /// </param>
    string IssueAccessToken(
        string privateKeyPem, string issuer, string audience, TimeSpan lifetime, Guid keyId, Guid tenantId, string roleType);

    IssuedRefreshToken IssueRefreshToken(string pepper, TimeSpan lifetime);
}
