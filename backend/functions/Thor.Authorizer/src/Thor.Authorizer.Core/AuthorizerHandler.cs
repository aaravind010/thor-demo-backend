using Microsoft.Extensions.Logging;
using Thor.Auth;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.Policy;
using Thor.Authorizer.Core.Tenancy;

namespace Thor.Authorizer.Core;

/// <summary>
/// Orchestrates the full authorization pipeline. Takes primitive inputs rather than raw AWS
/// event types, keeping Core fully decoupled from Amazon.Lambda.APIGatewayEvents. Fails closed:
/// any unresolved tenant, unrecognized credential, failed validation, or unhandled exception
/// anywhere in the pipeline results in Deny, never Allow.
/// </summary>
public sealed class AuthorizerHandler
{
    private readonly TenantResolver _tenantResolver;
    private readonly ICognitoValidator _cognitoValidator;
    private readonly ITokenValidator _thorTokenValidator;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly IPolicyBuilder _policyBuilder;
    private readonly ILogger<AuthorizerHandler> _logger;

    public AuthorizerHandler(
        TenantResolver tenantResolver,
        ICognitoValidator cognitoValidator,
        ITokenValidator thorTokenValidator,
        IApiKeyValidator apiKeyValidator,
        IPolicyBuilder policyBuilder,
        ILogger<AuthorizerHandler> logger)
    {
        _tenantResolver = tenantResolver;
        _cognitoValidator = cognitoValidator;
        _thorTokenValidator = thorTokenValidator;
        _apiKeyValidator = apiKeyValidator;
        _policyBuilder = policyBuilder;
        _logger = logger;
    }

    public async Task<PolicyDocument> HandleAsync(
        string? hostHeader, string? authorizationHeader, string methodArn)
    {
        try
        {
            var tenantResult = await _tenantResolver.ResolveAsync(hostHeader);
            if (!tenantResult.IsResolved)
            {
                _logger.LogWarning("Authorization denied: tenant not resolved for host");
                return _policyBuilder.Build(AuthResult.Deny("tenant not resolved"), methodArn);
            }

            var route = tenantResult.Route!;
            var classification = CredentialClassifier.Classify(authorizationHeader);

            var authResult = classification.Type switch
            {
                CredentialType.Jwt => await HandleJwtAsync(classification.Material!, route),
                CredentialType.ApiKey => await HandleApiKeyAsync(classification.Material!, route),
                _ => AuthResult.Deny("unrecognized credential"),
            };

            _logger.LogInformation(
                "Authorization decision={Decision} tenant_id={TenantId} caller_type={CallerType} principal_id={PrincipalId}",
                authResult.IsAllowed ? "Allow" : "Deny", route.TenantId, authResult.CallerType, authResult.PrincipalId);

            return _policyBuilder.Build(authResult, methodArn);
        }
        catch (Exception ex)
        {
            // Fail closed: ANY unhandled exception anywhere in the pipeline -> Deny.
            // Never log ex.Message or raw header values — type name only.
            _logger.LogError(ex, "Authorization denied due to unexpected exception: {ExceptionType}", ex.GetType().Name);
            return _policyBuilder.Build(AuthResult.Deny("internal error"), methodArn);
        }
    }

    /// <summary>
    /// Dispatches a JWT-shaped credential to the right validator based on its <c>iss</c> claim,
    /// read here without verifying the signature — a routing hint only (ADR §5), same as the
    /// Host-header subdomain. Anything other than Thor's own issuer falls back to the existing
    /// Cognito path unchanged; a forged <c>iss</c> only ever routes to the wrong validator, which
    /// then fails its own signature check.
    /// </summary>
    private Task<AuthResult> HandleJwtAsync(string token, TenantRoute route) =>
        JwtIssuerReader.TryReadIssuer(token) == TokenIssuers.ThorTaskApi
            ? Task.FromResult(HandleThorJwt(token, route))
            : HandleCognitoJwtAsync(token, route);

    private async Task<AuthResult> HandleCognitoJwtAsync(string token, TenantRoute route)
    {
        var result = await _cognitoValidator.ValidateAsync(token, route.UserPoolId, route.AppClientId, route.Region);
        return result.IsValid
            ? AuthResult.Success(route.TenantId, result.PrincipalId, "user", result.Scopes)
            : AuthResult.Deny(result.FailureReason ?? "jwt validation failed");
    }

    private AuthResult HandleThorJwt(string token, TenantRoute route)
    {
        var result = _thorTokenValidator.Validate(token);
        if (!result.IsValid)
        {
            return AuthResult.Deny("thor jwt validation failed");
        }

        // The Thor validator's signing key isn't tenant-scoped (unlike Cognito's per-pool JWKS),
        // so nothing else ties this token to the tenant resolved from the Host header — a token
        // minted for one tenant must not be honored against another's subdomain (ADR §1/§6).
        if (!string.Equals(result.TenantId.ToString(), route.TenantId, StringComparison.Ordinal))
        {
            return AuthResult.Deny("tenant mismatch");
        }

        return AuthResult.Success(route.TenantId, result.KeyId.ToString(), "machine", [result.Scope]);
    }

    private async Task<AuthResult> HandleApiKeyAsync(string keyMaterial, TenantRoute route)
    {
        var result = await _apiKeyValidator.ValidateAsync(route.TenantId, keyMaterial);
        return result.IsValid
            ? AuthResult.Success(route.TenantId, result.PrincipalId, "machine", result.Scopes)
            : AuthResult.Deny(result.FailureReason ?? "api key validation failed");
    }
}
