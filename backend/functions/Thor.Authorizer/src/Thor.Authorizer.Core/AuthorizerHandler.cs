using Microsoft.Extensions.Logging;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.Auth.Jwt;
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
    private readonly IJwtValidator _jwtValidator;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly IPolicyBuilder _policyBuilder;
    private readonly ILogger<AuthorizerHandler> _logger;

    public AuthorizerHandler(
        TenantResolver tenantResolver,
        IJwtValidator jwtValidator,
        IApiKeyValidator apiKeyValidator,
        IPolicyBuilder policyBuilder,
        ILogger<AuthorizerHandler> logger)
    {
        _tenantResolver = tenantResolver;
        _jwtValidator = jwtValidator;
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

    private async Task<AuthResult> HandleJwtAsync(string token, TenantRoute route)
    {
        var result = await _jwtValidator.ValidateAsync(token, route.UserPoolId, route.AppClientId, route.Region);
        return result.IsValid
            ? AuthResult.Success(route.TenantId, result.PrincipalId, "user", result.Scopes)
            : AuthResult.Deny(result.FailureReason ?? "jwt validation failed");
    }

    private async Task<AuthResult> HandleApiKeyAsync(string keyMaterial, TenantRoute route)
    {
        var result = await _apiKeyValidator.ValidateAsync(route.TenantId, keyMaterial);
        return result.IsValid
            ? AuthResult.Success(route.TenantId, result.PrincipalId, "machine", result.Scopes)
            : AuthResult.Deny(result.FailureReason ?? "api key validation failed");
    }
}
