using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Thor.Authorizer.Core.Auth.Jwt;

/// <summary>
/// Validates Cognito access tokens (never ID tokens) against the tenant's user pool JWKS.
/// </summary>
public sealed class CognitoJwtValidator : IJwtValidator
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(60);

    private readonly IJwksProvider _jwksProvider;

    public CognitoJwtValidator(IJwksProvider jwksProvider) => _jwksProvider = jwksProvider;

    public async Task<JwtValidationResult> ValidateAsync(
        string token, string userPoolId, string appClientId, string region)
    {
        try
        {
            var jwks = await _jwksProvider.GetJwksAsync(userPoolId, region);

            var handler = new JsonWebTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}",
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = ClockSkew,
                IssuerSigningKeys = jwks.Keys,
                ValidateIssuerSigningKey = true,
            };

            var result = await handler.ValidateTokenAsync(token, parameters);
            if (!result.IsValid)
            {
                return JwtValidationResult.Failure("signature/claims validation failed");
            }

            var claims = result.ClaimsIdentity!;

            if (claims.FindFirst("token_use")?.Value != "access")
            {
                return JwtValidationResult.Failure("not an access token");
            }

            if (claims.FindFirst("client_id")?.Value != appClientId)
            {
                return JwtValidationResult.Failure("client_id mismatch");
            }

            var sub = claims.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(sub))
            {
                return JwtValidationResult.Failure("missing sub claim");
            }

            var scopeClaim = claims.FindFirst("scope")?.Value ?? string.Empty;
            var scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return JwtValidationResult.Success(sub, scopes);
        }
        catch (Exception ex)
        {
            // Fail closed: any unexpected exception during validation is a Deny.
            // Never include ex.Message (could echo token/claim fragments) — type name only.
            return JwtValidationResult.Failure($"unexpected validation error: {ex.GetType().Name}");
        }
    }
}
