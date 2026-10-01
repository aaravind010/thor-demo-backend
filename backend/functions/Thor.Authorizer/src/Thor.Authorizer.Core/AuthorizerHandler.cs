using Microsoft.Extensions.Logging;
using Thor.Auth;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.Policy;
using Thor.Authorizer.Core.Tenancy;

namespace Thor.Authorizer.Core;

/// <summary>
/// Orchestrates the full authorization pipeline. Takes primitive inputs rather than raw AWS
/// event types, keeping Core fully decoupled from Amazon.Lambda.APIGatewayEvents. Fails closed:
/// any unresolved tenant, unrecognized credential, failed validation, or unhandled exception
/// anywhere in the pipeline results in Deny, never Allow.
/// <para>
/// The tenant comes only from the credential (ADR §5) — never from Host or any other header,
/// which behind CloudFront/API Gateway is either not the tenant's or caller-controlled:
/// a Cognito token's pool, a Thor-issued token's verified tenant claim, or an API key's stored
/// record.
/// </para>
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
        string? authorizationHeader, string methodArn, string? httpMethod = null, string? path = null)
    {
        try
        {
            if (ExemptRoutes.SkipsCredentialValidation(httpMethod, path))
            {
                return HandleExemptRoute(methodArn);
            }

            var classification = CredentialClassifier.Classify(authorizationHeader);

            var authResult = classification.Type switch
            {
                CredentialType.Jwt => await HandleJwtAsync(classification.Material!),
                CredentialType.ApiKey => await HandleApiKeyAsync(classification.Material!),
                _ => AuthResult.Deny("unrecognized credential"),
            };

            _logger.LogInformation(
                "Authorization decision={Decision} tenant_id={TenantId} caller_type={CallerType} principal_id={PrincipalId}",
                authResult.IsAllowed ? "Allow" : "Deny", authResult.TenantId, authResult.CallerType, authResult.PrincipalId);

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
    /// read here without verifying the signature — a routing hint only (ADR §5). Anything other
    /// than Thor's own issuer takes the Cognito path; a forged <c>iss</c> only ever routes to the
    /// wrong validator or tenant pool, which then fails its own signature check.
    /// </summary>
    private Task<AuthResult> HandleJwtAsync(string token)
    {
        var issuer = JwtIssuerReader.TryReadIssuer(token);
        return issuer == TokenIssuers.ThorTaskApi
            ? HandleThorJwtAsync(token)
            : HandleCognitoJwtAsync(token, issuer);
    }

    /// <summary>
    /// One user pool per tenant (ADR §5), so the pool named in <c>iss</c> identifies the tenant.
    /// The validator then pins the issuer to that tenant's pool/region and checks the signature
    /// against that pool's JWKS, so the token must genuinely belong to the resolved tenant.
    /// </summary>
    private async Task<AuthResult> HandleCognitoJwtAsync(string token, string? issuer)
    {
        var cognitoIssuer = CognitoIssuerParser.TryParse(issuer);
        if (cognitoIssuer is null)
        {
            return AuthResult.Deny("unrecognized token issuer");
        }

        var tenantResult = await _tenantResolver.ResolveByUserPoolIdAsync(cognitoIssuer.UserPoolId);
        if (!tenantResult.IsResolved)
        {
            _logger.LogWarning("Authorization denied: tenant not resolved for user_pool_id={UserPoolId}", cognitoIssuer.UserPoolId);
            return AuthResult.Deny("tenant not resolved");
        }

        var route = tenantResult.Route!;
        if (!string.Equals(route.Region, cognitoIssuer.Region, StringComparison.Ordinal))
        {
            return AuthResult.Deny("issuer region mismatch");
        }

        var result = await _cognitoValidator.ValidateAsync(token, route.UserPoolId, route.AppClientId, route.Region);
        return result.IsValid
            ? AuthResult.Success(route.TenantId, result.PrincipalId, "user", result.Scopes, result.Groups)
            : AuthResult.Deny(result.FailureReason ?? "jwt validation failed");
    }

    /// <summary>
    /// The Thor validator's signing key isn't tenant-scoped, so the tenant is the token's own
    /// (signature-verified) tenant claim — which must still name a tenant that exists.
    /// </summary>
    private async Task<AuthResult> HandleThorJwtAsync(string token)
    {
        var result = _thorTokenValidator.Validate(token);
        if (!result.IsValid)
        {
            return AuthResult.Deny("thor jwt validation failed");
        }

        var tenantResult = await _tenantResolver.ResolveByTenantIdAsync(result.TenantId.ToString());
        if (!tenantResult.IsResolved)
        {
            _logger.LogWarning("Authorization denied: tenant not resolved for tenant_id={TenantId}", result.TenantId);
            return AuthResult.Deny("tenant not resolved");
        }

        return AuthResult.Success(tenantResult.Route!.TenantId, result.KeyId.ToString(), "machine", [result.Scope]);
    }

    private async Task<AuthResult> HandleApiKeyAsync(string keyMaterial)
    {
        var result = await _apiKeyValidator.ValidateAsync(keyMaterial);
        return result.IsValid
            ? AuthResult.Success(result.TenantId, result.PrincipalId, "machine", result.Scopes)
            : AuthResult.Deny(result.FailureReason ?? "api key validation failed");
    }

    /// <summary>
    /// TEMPORARY: allows an ExemptRoutes match without verifying any credential. With no verified
    /// credential there is no trusted tenant either, so tenant_id is always empty — none of the
    /// exempt routes (health, docs, register) needs one.
    /// </summary>
    private PolicyDocument HandleExemptRoute(string methodArn)
    {
        var authResult = AuthResult.Success(tenantId: string.Empty, principalId: "exempt", callerType: "exempt", scopes: []);

        _logger.LogInformation("Authorization decision=Allow (exempt route, no credential verified)");

        return _policyBuilder.Build(authResult, methodArn);
    }
}
