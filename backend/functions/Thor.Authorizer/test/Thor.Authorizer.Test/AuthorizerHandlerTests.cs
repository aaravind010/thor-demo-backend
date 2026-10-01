using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.Auth;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.Policy;
using Thor.Authorizer.Core.Tenancy;
using Thor.Authorizer.Core.Tenancy.Interface;
using Thor.Authorizer.Test.TestFixtures;

namespace Thor.Authorizer.Test;

public class AuthorizerHandlerTests
{
    private const string MethodArn = "arn:aws:execute-api:us-east-1:123456789012:abc123/prod/GET/orders";

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

    private static string CognitoTokenFor(TenantRoute route) =>
        BuildJwtWithIssuer(TenantRouteFixtures.CognitoIssuerFor(route));

    private void AssertDenied() =>
        _policyBuilder.Received(1).Build(Arg.Is<AuthResult>(r => r != null && !r.IsAllowed), MethodArn);

    [Fact]
    public async Task HandleAsync_CognitoPoolNotMappedToAnyTenant_DeniesWithoutValidatingToken()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetByUserPoolIdAsync(route.UserPoolId).Returns((TenantRoute?)null);

        await CreateHandler().HandleAsync($"Bearer {CognitoTokenFor(route)}", MethodArn);

        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
        AssertDenied();
    }

    [Theory]
    [InlineData("https://evil.example.com/us-east-1_ExamplePool")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/eu-west-1_ExamplePool")]
    [InlineData("https://cognito-idp.us-east-1.amazonaws.com/us-east-1_Pool/extra")]
    [InlineData("not-a-url")]
    public async Task HandleAsync_NonCognitoIssuer_DeniesWithoutAnyTenantLookup(string issuer)
    {
        await CreateHandler().HandleAsync($"Bearer {BuildJwtWithIssuer(issuer)}", MethodArn);

        await _tenantRoutingCache.DidNotReceiveWithAnyArgs().GetByUserPoolIdAsync(default!);
        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_JwtWithoutReadableIssuer_DeniesWithoutAnyTenantLookup()
    {
        await CreateHandler().HandleAsync("Bearer eyJh.eyJz.c2ln", MethodArn);

        await _tenantRoutingCache.DidNotReceiveWithAnyArgs().GetByUserPoolIdAsync(default!);
        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_IssuerRegionDiffersFromTenantRouteRegion_Denies()
    {
        var route = TenantRouteFixtures.Default() with { Region = "eu-west-1" };
        _tenantRoutingCache.GetByUserPoolIdAsync(route.UserPoolId).Returns(route);
        var token = BuildJwtWithIssuer($"https://cognito-idp.us-east-1.amazonaws.com/{route.UserPoolId}");

        await CreateHandler().HandleAsync($"Bearer {token}", MethodArn);

        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_UnrecognizedCredential_Denies()
    {
        await CreateHandler().HandleAsync("garbage-header", MethodArn);

        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_WhenTenantCacheThrows_DeniesInsteadOfPropagating()
    {
        _tenantRoutingCache.GetByUserPoolIdAsync(Arg.Any<string>()).Returns<Task<TenantRoute?>>(_ => throw new InvalidOperationException("boom"));

        var act = async () => await CreateHandler().HandleAsync($"Bearer {CognitoTokenFor(TenantRouteFixtures.Default())}", MethodArn);

        await act.Should().NotThrowAsync();
        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_WhenJwtValidatorThrows_DeniesInsteadOfPropagating()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetByUserPoolIdAsync(route.UserPoolId).Returns(route);
        _cognitoValidator.ValidateAsync(default!, default!, default!, default!).ReturnsForAnyArgs<Task<JwtValidationResult>>(_ => throw new InvalidOperationException("boom"));

        var act = async () => await CreateHandler().HandleAsync($"Bearer {CognitoTokenFor(route)}", MethodArn);

        await act.Should().NotThrowAsync();
        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_WhenApiKeyValidatorThrows_DeniesInsteadOfPropagating()
    {
        _apiKeyValidator.ValidateAsync(default!).ReturnsForAnyArgs<Task<ApiKeyValidationResult>>(_ => throw new InvalidOperationException("boom"));

        var act = async () => await CreateHandler().HandleAsync("ApiKey key-1.secret", MethodArn);

        await act.Should().NotThrowAsync();
        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_SuccessfulCognitoPath_ResolvesTenantFromIssuerPoolAndBuildsAllow()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetByUserPoolIdAsync(route.UserPoolId).Returns(route);
        var token = CognitoTokenFor(route);
        _cognitoValidator.ValidateAsync(token, route.UserPoolId, route.AppClientId, route.Region)
            .Returns(JwtValidationResult.Success("principal-1", ["read:orders"]));

        await CreateHandler().HandleAsync($"Bearer {token}", MethodArn);

        _thorTokenValidator.DidNotReceiveWithAnyArgs().Validate(default!);
        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == route.TenantId && r.PrincipalId == "principal-1" && r.CallerType == "user"),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulCognitoPath_PropagatesCognitoGroups()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetByUserPoolIdAsync(route.UserPoolId).Returns(route);
        _cognitoValidator.ValidateAsync(Arg.Any<string>(), route.UserPoolId, route.AppClientId, route.Region)
            .Returns(JwtValidationResult.Success("principal-1", [], ["admins"]));

        await CreateHandler().HandleAsync($"Bearer {CognitoTokenFor(route)}", MethodArn);

        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.Groups.SequenceEqual(new[] { "admins" })),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_CognitoValidationFails_Denies()
    {
        var route = TenantRouteFixtures.Default();
        _tenantRoutingCache.GetByUserPoolIdAsync(route.UserPoolId).Returns(route);
        _cognitoValidator.ValidateAsync(Arg.Any<string>(), route.UserPoolId, route.AppClientId, route.Region)
            .Returns(JwtValidationResult.Failure("signature invalid"));

        await CreateHandler().HandleAsync($"Bearer {CognitoTokenFor(route)}", MethodArn);

        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_SuccessfulApiKeyPath_UsesTenantFromKeyRecord()
    {
        _apiKeyValidator.ValidateAsync("key-1.secret")
            .Returns(ApiKeyValidationResult.Success("tenant-from-key", "principal-2", ["read:orders"]));

        await CreateHandler().HandleAsync("ApiKey key-1.secret", MethodArn);

        // Machine callers never carry user-pool groups, so they can never pass an admin check.
        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == "tenant-from-key" && r.PrincipalId == "principal-2"
                && r.CallerType == "machine" && r.Groups.Count == 0),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_SuccessfulThorJwtPath_ResolvesTenantFromTokenClaim()
    {
        var tenantId = Guid.NewGuid();
        var keyId = Guid.NewGuid();
        var route = TenantRouteFixtures.Default() with { TenantId = tenantId.ToString() };
        _tenantRoutingCache.GetByTenantIdAsync(tenantId.ToString()).Returns(route);
        var token = BuildJwtWithIssuer(TokenIssuers.ThorTaskApi);
        _thorTokenValidator.Validate(token).Returns(TokenValidationResult.Success(keyId, tenantId, "scanner"));

        await CreateHandler().HandleAsync($"Bearer {token}", MethodArn);

        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == route.TenantId && r.PrincipalId == keyId.ToString() && r.CallerType == "machine"
                && r.Scopes.Count == 1 && r.Scopes[0] == "scanner"),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_ThorJwtPath_UnknownTenant_Denies()
    {
        _tenantRoutingCache.GetByTenantIdAsync(Arg.Any<string>()).Returns((TenantRoute?)null);
        var token = BuildJwtWithIssuer(TokenIssuers.ThorTaskApi);
        _thorTokenValidator.Validate(token).Returns(TokenValidationResult.Success(Guid.NewGuid(), Guid.NewGuid(), "scanner"));

        await CreateHandler().HandleAsync($"Bearer {token}", MethodArn);

        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_ThorJwtPath_FailedValidation_DeniesWithoutTenantLookup()
    {
        var token = BuildJwtWithIssuer(TokenIssuers.ThorTaskApi);
        _thorTokenValidator.Validate(token).Returns(TokenValidationResult.Invalid);

        await CreateHandler().HandleAsync($"Bearer {token}", MethodArn);

        await _tenantRoutingCache.DidNotReceiveWithAnyArgs().GetByTenantIdAsync(default!);
        AssertDenied();
    }

    [Fact]
    public async Task HandleAsync_WhenThorTokenValidatorThrows_DeniesInsteadOfPropagating()
    {
        var token = BuildJwtWithIssuer(TokenIssuers.ThorTaskApi);
        _thorTokenValidator.Validate(token).Returns(_ => throw new InvalidOperationException("boom"));

        var act = async () => await CreateHandler().HandleAsync($"Bearer {token}", MethodArn);

        await act.Should().NotThrowAsync();
        AssertDenied();
    }

    [Theory]
    [InlineData("POST", "/v1/register")]
    [InlineData("POST", "/v1/connector-api-keys")]
    [InlineData("GET", "/health")]
    public async Task HandleAsync_ExemptRoute_AllowsWithoutCredentialValidationOrTenant(string httpMethod, string path)
    {
        await CreateHandler().HandleAsync(authorizationHeader: null, MethodArn, httpMethod, path);

        await _cognitoValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
        _thorTokenValidator.DidNotReceiveWithAnyArgs().Validate(default!);
        await _apiKeyValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!);
        await _tenantRoutingCache.DidNotReceiveWithAnyArgs().GetByUserPoolIdAsync(default!);
        await _tenantRoutingCache.DidNotReceiveWithAnyArgs().GetByTenantIdAsync(default!);
        _policyBuilder.Received(1).Build(
            Arg.Is<AuthResult>(r => r != null && r.IsAllowed && r.TenantId == string.Empty && r.CallerType == "exempt"),
            MethodArn);
    }

    [Fact]
    public async Task HandleAsync_NonExemptRoute_DoesNotSkipCredentialValidation()
    {
        _tenantRoutingCache.GetByUserPoolIdAsync(Arg.Any<string>()).Returns((TenantRoute?)null);

        await CreateHandler().HandleAsync($"Bearer {CognitoTokenFor(TenantRouteFixtures.Default())}", MethodArn, httpMethod: "GET", path: "/v1/orders");

        AssertDenied();
    }
}
