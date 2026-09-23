using Thor.Auth;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataConnectionManager.Routing;
using Thor.DataLayer.Models;

namespace Thor.Api.Middleware;

/// <summary>
/// Requires a valid Cognito-issued bearer JWT on every gated request, verified against the
/// requesting tenant's user pool. The tenant is resolved from the Host subdomain — a hint used
/// only to pick which user pool to check against; the token still has to verify against that
/// pool's JWKS (ADR §5: never trust a spoofable input as authority on its own). Fails closed:
/// a missing/malformed Authorization header, an unresolved tenant subdomain, or a failed
/// validation are all rejected with 401.
/// </summary>
public sealed class CognitoAuthMiddleware(
    RequestDelegate next, ICognitoValidator cognitoValidator, ITenantRoutingResolver tenantRoutingResolver)
{
    private const string BearerPrefix = "Bearer ";
    private const int MinLabelsForTenantDomain = 3;

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var subdomain = ExtractSubdomain(context.Request.Host.Value);
        if (subdomain is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        TenantRouting route;
        try
        {
            route = await tenantRoutingResolver.ResolveBySubdomainAsync(subdomain, context.RequestAborted);
        }
        catch (TenantNotFoundException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var token = authHeader[BearerPrefix.Length..];
        var result = await cognitoValidator.ValidateAsync(token, route.UserPoolId, route.AppClientId, route.Region);

        if (!result.IsValid)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    /// <summary>
    /// Extracts the leftmost label of a Host header as the tenant subdomain, e.g.
    /// "acme.api.thor.example.com" -> "acme". Returns null if the host has too few
    /// labels to contain a tenant subdomain or is malformed.
    /// </summary>
    private static string? ExtractSubdomain(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            return null;
        }

        var hostOnly = hostHeader.Split(':')[0].Trim();
        var labels = hostOnly.Split('.', StringSplitOptions.RemoveEmptyEntries);

        return labels.Length < MinLabelsForTenantDomain ? null : labels[0].ToLowerInvariant();
    }
}
