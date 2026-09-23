using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.Auth;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.Policy;
using Thor.Authorizer.Core.Tenancy;
using Thor.Authorizer.Core.Tenancy.Interface;
using Thor.Authorizer.Test.TestFixtures;

namespace Thor.Authorizer.Test;

public class AuthorizerHandlerTests
{
    private const string MethodArn = "arn:aws:execute-api:us-east-1:123456789012:abc123/prod/GET/orders";
    private const string HostHeader = "acme.api.thor.example.com";

    private readonly ITenantRoutingCache _tenantRoutingCache = Substitute.For<ITenantRoutingCache>();
    private readonly ICognitoValidator _cognitoValidator = Substitute.For<ICognitoValidator>();
    private readonly ITokenValidator _thorTokenValidator = Substitute.For<ITokenValidator>();
    private readonly IApiKeyValidator _apiKeyValidator = Substitute.For<IApiKeyValidator>();
    private readonly IPolicyBuilder _policyBuilder = Substitute.For<IPolicyBuilder>();

    private AuthorizerHandler CreateHandler()
    {
        var resolver = new TenantResolver(_tenantRoutingCache);
        return new AuthorizerHandler(resolver, _cognitoValidator, _thorTokenValidator, _apiKeyValidator, _policyBuilder, NullLogger<AuthorizerHandler>.Instance);
    }

    /// <summary>
    /// Builds a compact JWT (unsigned — the authorizer only peeks its `iss` claim to decide
    /// routing before handing the raw token to a mocked validator) carrying the given issuer.
    /// </summary>
    private static string BuildJwtWithIssuer(string issuer)
    {
        static string Segment(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = Segment("""{"alg":"none","typ":"JWT"}""");
        var payload = Segment($$"""{"iss":"{{issuer}}"}""");
        return $"{header}.{payload}.sig";
    }

    [Fact]
    public async Task HandleAsync_UnresolvedTenant_DeniesWithoutAttemptingCredentialValidation()
    {
        _tenantRoutingCache.GetOrAddAsync(Arg.Any<string>()).Returns((Thor.Authorizer.Core.DataAccess.TenantRoute?)null);
        _policyBuilder.Build(Arg.Any<AuthResult>(), MethodArn).Returns(new PolicyDocument("unauthorized", "Deny", MethodArn, new Dictionary<string, string>()));

        await CreateHandler().HandleAsync(HostHeader, "Bearer some.jwt.token", MethodArn);

        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
        _thorTokenValidator.DidNotReceiveWithAnyArgs().Validate(default!);
        await _apiKeyValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!);
        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_UnrecognizedCredential_Denies()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);

        await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", "garbage-header", MethodArn);

        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_WhenTenantCacheThrows_DeniesInsteadOfPropagating()
    {
        _tenantRoutingCache.GetOrAddAsync(Arg.Any<string>()).Returns<Task<Thor.Authorizer.Core.DataAccess.TenantRoute?>>(_ => throw new InvalidOperationException("boom"));

        var act = async () => await CreateHandler().HandleAsync(HostHeader, "Bearer some.jwt.token", MethodArn);

        await act.Should().NotThrowAsync();
        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_WhenJwtValidatorThrows_DeniesInsteadOfPropagating()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        _cognitoValidator.ValidateAsync(default!, default!, default!, default!).ReturnsForAnyArgs<Task<JwtValidationResult>>(_ => throw new InvalidOperationException("boom"));

        var act = async () => await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", "Bearer eyJh.eyJz.c2ln", MethodArn);

        await act.Should().NotThrowAsync();
        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_WhenApiKeyValidatorThrows_DeniesInsteadOfPropagating()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        _apiKeyValidator.ValidateAsync(default!, default!).ReturnsForAnyArgs<Task<ApiKeyValidationResult>>(_ => throw new InvalidOperationException("boom"));

        var act = async () => await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", "ApiKey key-1.secret", MethodArn);

        await act.Should().NotThrowAsync();
        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulJwtPath_BuildsAllowWithExpectedFields()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        _cognitoValidator.ValidateAsync(Arg.Any<string>(), route.UserPoolId, route.AppClientId, route.Region)
            .Returns(JwtValidationResult.Success("principal-1", ["read:orders"]));

        await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", "Bearer eyJh.eyJz.c2ln", MethodArn);

        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == route.TenantId && r.PrincipalId == "principal-1" && r.CallerType == "user"),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulApiKeyPath_BuildsAllowWithExpectedFields()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        _apiKeyValidator.ValidateAsync(route.TenantId, "key-1.secret")
            .Returns(ApiKeyValidationResult.Success("principal-2", ["read:orders"]));

        await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", "ApiKey key-1.secret", MethodArn);

        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == route.TenantId && r.PrincipalId == "principal-2" && r.CallerType == "machine"),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulThorJwtPath_BuildsAllowWithExpectedFields()
    {
        var tenantId = Guid.NewGuid();
        var keyId = Guid.NewGuid();
        var route = TenantRouteFixtures.Default() with { TenantId = tenantId.ToString() };
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        var token = BuildJwtWithIssuer(TokenIssuers.ThorTaskApi);
        _thorTokenValidator.Validate(token).Returns(TokenValidationResult.Success(keyId, tenantId, "scanner"));

        await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", $"Bearer {token}", MethodArn);

        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == route.TenantId && r.PrincipalId == keyId.ToString() && r.CallerType == "machine"
                && r.Scopes.Count == 1 && r.Scopes[0] == "scanner"),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_ThorJwtPath_TenantMismatch_Denies()
    {
        var route = TenantRouteFixtures.Default() with { TenantId = Guid.NewGuid().ToString() };
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        var token = BuildJwtWithIssuer(TokenIssuers.ThorTaskApi);
        _thorTokenValidator.Validate(token).Returns(TokenValidationResult.Success(Guid.NewGuid(), Guid.NewGuid(), "scanner"));

        await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", $"Bearer {token}", MethodArn);

        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_ThorJwtPath_FailedValidation_Denies()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        var token = BuildJwtWithIssuer(TokenIssuers.ThorTaskApi);
        _thorTokenValidator.Validate(token).Returns(TokenValidationResult.Invalid);

        await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", $"Bearer {token}", MethodArn);

        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_WhenThorTokenValidatorThrows_DeniesInsteadOfPropagating()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        var token = BuildJwtWithIssuer(TokenIssuers.ThorTaskApi);
        _thorTokenValidator.Validate(token).Returns(_ => throw new InvalidOperationException("boom"));

        var act = async () => await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", $"Bearer {token}", MethodArn);

        await act.Should().NotThrowAsync();
        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_ExemptRouteWithResolvableTenant_AllowsWithoutCredentialValidationAndResolvesTenant()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);

        await CreateHandler().HandleAsync(
            $"{route.Subdomain}.api.thor.example.com", authorizationHeader: null, MethodArn, httpMethod: "POST", path: "/v1/connector-api-keys");

        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
        _thorTokenValidator.DidNotReceiveWithAnyArgs().Validate(default!);
        await _apiKeyValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!);
        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == route.TenantId && r.CallerType == "exempt"),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_ExemptRouteWithUnresolvableTenant_StillAllows()
    {
        _tenantRoutingCache.GetOrAddAsync(Arg.Any<string>()).Returns((Thor.Authorizer.Core.DataAccess.TenantRoute?)null);

        await CreateHandler().HandleAsync(
            hostHeader: null, authorizationHeader: null, MethodArn, httpMethod: "GET", path: "/health");

        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == string.Empty && r.CallerType == "exempt"),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_NonExemptRoute_DoesNotSkipCredentialValidation()
    {
        _tenantRoutingCache.GetOrAddAsync(Arg.Any<string>()).Returns((Thor.Authorizer.Core.DataAccess.TenantRoute?)null);

        await CreateHandler().HandleAsync(HostHeader, "Bearer some.jwt.token", MethodArn, httpMethod: "GET", path: "/v1/orders");

        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);
    }

    [Fact]
    public async Task HandleAsync_JwtWithNonThorIssuer_UsesCognitoPathNotThorValidator()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetOrAddAsync(route.Subdomain).Returns(route);
        var token = BuildJwtWithIssuer("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_ExamplePool");
        _cognitoValidator.ValidateAsync(token, route.UserPoolId, route.AppClientId, route.Region)
            .Returns(JwtValidationResult.Success("principal-3", ["read:orders"]));

        await CreateHandler().HandleAsync($"{route.Subdomain}.api.thor.example.com", $"Bearer {token}", MethodArn);

        _thorTokenValidator.DidNotReceiveWithAnyArgs().Validate(default!);
        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.PrincipalId == "principal-3" && r.CallerType == "user"),
            MethodArn);
    }
}
