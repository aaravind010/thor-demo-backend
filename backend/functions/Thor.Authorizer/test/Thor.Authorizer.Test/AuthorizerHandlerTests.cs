using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.Auth.Jwt;
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
    private readonly IJwtValidator _jwtValidator = Substitute.For<IJwtValidator>();
    private readonly IApiKeyValidator _apiKeyValidator = Substitute.For<IApiKeyValidator>();
    private readonly IPolicyBuilder _policyBuilder = Substitute.For<IPolicyBuilder>();

    private AuthorizerHandler CreateHandler()
    {
        var resolver = new TenantResolver(_tenantRoutingCache);
        return new AuthorizerHandler(resolver, _jwtValidator, _apiKeyValidator, _policyBuilder, NullLogger<AuthorizerHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_UnresolvedTenant_DeniesWithoutAttemptingCredentialValidation()
    {
        _tenantRoutingCache.GetOrAddAsync(Arg.Any<string>()).Returns((Thor.Authorizer.Core.DataAccess.TenantRoute?)null);
        _policyBuilder.Build(Arg.Any<AuthResult>(), MethodArn).Returns(new PolicyDocument("unauthorized", "Deny", MethodArn, new Dictionary<string, string>()));

        await CreateHandler().HandleAsync(HostHeader, "Bearer some.jwt.token", MethodArn);

        await _jwtValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default!, default!);
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
        _jwtValidator.ValidateAsync(default!, default!, default!, default!).ReturnsForAnyArgs<Task<JwtValidationResult>>(_ => throw new InvalidOperationException("boom"));

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
        _jwtValidator.ValidateAsync(Arg.Any<string>(), route.UserPoolId, route.AppClientId, route.Region)
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
}
