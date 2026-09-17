using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Thor.Auth;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.DataAccess.Fakes;
using Thor.Authorizer.Core.Policy;
using Thor.Authorizer.Core.Tenancy;
using Thor.Authorizer.Core.Tenancy.Interface;
using Thor.Authorizer.Test.TestFixtures;

namespace Thor.Authorizer.Test;

/// <summary>
/// Exercises the real wiring end-to-end: Function -> AuthorizerHandler -> Core components ->
/// PolicyDocument -> APIGatewayCustomAuthorizerResponse, using the API key path (no network
/// dependency, unlike the JWT/JWKS path) with a DI container seeded with real test data instead
/// of CompositionRoot's empty defaults.
/// </summary>
public class FunctionEndToEndTests
{
    private const string MethodArn = "arn:aws:execute-api:us-east-1:123456789012:abc123/prod/GET/orders";

    private static IServiceProvider BuildSeededServiceProvider(TenantRoute route, ApiKeyRecord apiKey)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient("CognitoJwks");

        services.AddSingleton<ITenantRoutingRepository>(new InMemoryTenantRoutingRepository([route]));
        services.AddSingleton<IApiKeyRepository>(new InMemoryApiKeyRepository([apiKey]));

        services.AddSingleton(new TenantRoutingCacheOptions { Ttl = TimeSpan.FromMinutes(10) });
        services.AddSingleton<ITenantRoutingCache, TenantRoutingCache>();
        services.AddSingleton<TenantResolver>();

        // JWT path is not exercised here (would require a live/mocked JWKS endpoint), but is
        // still wired so AuthorizerHandler's constructor resolves.
        services.AddSingleton<IJwksProvider>(Substitute.For<IJwksProvider>());
        services.AddSingleton<ICognitoValidator, CognitoJwtValidator>();
        services.AddSingleton(Substitute.For<ITokenValidator>());

        services.AddSingleton(TestSaltProvider.Create());
        services.AddSingleton<IApiKeyHasher, Pbkdf2ApiKeyHasher>();
        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();

        services.AddSingleton<IPolicyBuilder, IamPolicyBuilder>();
        services.AddSingleton<AuthorizerHandler>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FunctionHandler_ValidApiKey_ReturnsAllowPolicyWithExpectedContext()
    {
        var hasher = new Pbkdf2ApiKeyHasher(TestSaltProvider.Create());
        var route = new TenantRoute("tenant-1", "acme", "us-east-1_ExamplePool", "client-abc123", "us-east-1");
        var apiKey = new ApiKeyRecord("key-1", route.TenantId, await hasher.HashAsync("s3cr3t"), "active", ExpiresAt: null, "principal-1");

        var function = new Thor.Authorizer.Function.Function(BuildSeededServiceProvider(route, apiKey));

        var request = new APIGatewayCustomAuthorizerRequest
        {
            MethodArn = MethodArn,
            Headers = new Dictionary<string, string>
            {
                ["Host"] = "acme.api.thor.example.com",
                ["Authorization"] = "ApiKey key-1.s3cr3t",
            },
        };

        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        response.PolicyDocument.Statement.Should().ContainSingle(s => s.Effect == "Allow" && s.Resource.Contains(MethodArn));
        response.PrincipalID.Should().Be("principal-1");
        response.Context["tenant_id"].Should().Be("tenant-1");
        response.Context["caller_type"].Should().Be("machine");
        response.Context["principal_id"].Should().Be("principal-1");
    }

    [Fact]
    public async Task FunctionHandler_UnknownTenant_ReturnsDenyPolicy()
    {
        var hasher = new Pbkdf2ApiKeyHasher(TestSaltProvider.Create());
        var route = new TenantRoute("tenant-1", "acme", "us-east-1_ExamplePool", "client-abc123", "us-east-1");
        var apiKey = new ApiKeyRecord("key-1", route.TenantId, await hasher.HashAsync("s3cr3t"), "active", ExpiresAt: null, "principal-1");

        var function = new Thor.Authorizer.Function.Function(BuildSeededServiceProvider(route, apiKey));

        var request = new APIGatewayCustomAuthorizerRequest
        {
            MethodArn = MethodArn,
            Headers = new Dictionary<string, string>
            {
                ["Host"] = "unknown-tenant.api.thor.example.com",
                ["Authorization"] = "ApiKey key-1.s3cr3t",
            },
        };

        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        response.PolicyDocument.Statement.Should().ContainSingle(s => s.Effect == "Deny");
        response.Context.Should().BeEmpty();
    }
}
