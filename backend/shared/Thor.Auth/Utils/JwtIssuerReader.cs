using Microsoft.IdentityModel.JsonWebTokens;

namespace Thor.Auth;

/// <summary>
/// Reads the <c>iss</c> claim from a compact JWT without verifying its signature — used only to
/// pick which validator to run against a bearer token whose issuer isn't yet known (e.g. the
/// Lambda authorizer dispatching between Cognito and Thor-issued tokens). Never a source of
/// authority per ADR §5: the value returned here is a routing hint only, exactly like a subdomain
/// or header — each validator still performs its own full signature/claims check before granting
/// access, so a forged <c>iss</c> at most routes to the wrong validator and fails there.
/// </summary>
public static class JwtIssuerReader
{
    public static string? TryReadIssuer(string token)
    {
        try
        {
            return new JsonWebTokenHandler().ReadJsonWebToken(token).Issuer;
        }
        catch
        {
            return null;
        }
    }
}
