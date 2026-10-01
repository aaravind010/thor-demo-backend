using Thor.Api.Constants;
using Thor.Auth;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models;

namespace Thor.Api.Middleware;

/// <summary>
/// Re-verifies the bearer token in-process on every gated request — defence in depth behind the
/// Lambda authorizer, so a request that reaches Thor.Api without passing API Gateway still needs
/// a valid credential for the tenant it claims. Fails closed with 401.
/// <para>
/// The tenant comes from <see cref="TenantConstants.TenantHeaderName"/> (overwritten by API Gateway
/// from the authorizer's context, which derives it from the credential itself), not the Host
/// header. The header is still only a hint here — the token must verify against that tenant, so a
/// claimed tenant it doesn't belong to is rejected:
/// </para>
/// <list type="bullet">
/// <item>Thor-issued connector JWTs (e.g. the scan scheduler) are verified with
/// <see cref="ITokenValidator"/> and their tenant claim must equal the header.</item>
/// <item>Anything else is verified as a Cognito access token against that tenant's user pool.</item>
/// </list>
/// Mirrors the Lambda authorizer's issuer-based dispatch (AuthorizerHandler.HandleJwtAsync).
/// </summary>
public sealed class CognitoAuthMiddleware(
    RequestDelegate next,
    ICognitoValidator cognitoValidator,
    ITokenValidator thorTokenValidator,
    ITenantRoutingResolver tenantRoutingResolver,
    ILogger<CognitoAuthMiddleware> logger)
{
    private const string BearerPrefix = "Bearer ";

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith(BearerPrefix, StringComparison.Ordinal) ||
            !Guid.TryParse(context.Request.Headers[TenantConstants.TenantHeaderName], out var tenantId))
        {
            Reject(context, "missing bearer token or tenant header");
            return;
        }

        var token = authHeader[BearerPrefix.Length..];

        var isValid = JwtIssuerReader.TryReadIssuer(token) == TokenIssuers.ThorTaskApi
            ? IsValidThorToken(token, tenantId)
            : await IsValidCognitoTokenAsync(token, tenantId, context.RequestAborted);

        if (!isValid)
        {
            Reject(context, "token validation failed");
            return;
        }

        await next(context);
    }

    private bool IsValidThorToken(string token, Guid tenantId)
    {
        var result = thorTokenValidator.Validate(token);
        return result.IsValid && result.TenantId == tenantId;
    }

    private async Task<bool> IsValidCognitoTokenAsync(string token, Guid tenantId, CancellationToken cancellationToken)
    {
        TenantRouting route;
        try
        {
            route = await tenantRoutingResolver.ResolveAsync(tenantId, cancellationToken);
        }
        catch (TenantNotFoundException)
        {
            return false;
        }

        var result = await cognitoValidator.ValidateAsync(token, route.UserPoolId, route.AppClientId, route.Region);
        return result.IsValid;
    }

    private void Reject(HttpContext context, string reason)
    {
        // Never log the token or header values — the reason and path only.
        logger.LogWarning("Request rejected by auth middleware: {Reason} path={Path}", reason, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }
}
