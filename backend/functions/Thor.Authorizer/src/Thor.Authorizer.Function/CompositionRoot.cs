using Amazon.Lambda.Logging.AspNetCore;
using Amazon.SecretsManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.Auth.Jwt;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.DataAccess.Fakes;
using Thor.Authorizer.Core.Policy;
using Thor.Authorizer.Core.Tenancy;
using Thor.Authorizer.Core.Tenancy.Interface;

namespace Thor.Authorizer.Function;

/// <summary>
/// Wires Core services for the Lambda runtime. DataAccess is wired to the in-memory fakes for
/// now as DB-backed repository is to be built and swapped in when available
/// </summary>
internal static class CompositionRoot
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddLambdaLogger());
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient("CognitoJwks");

        // TODO: replace with real DB-backed implementations once the separate DataAccess
        // package is available. Empty seed data — dev-only placeholder, must not ship as-is.
        services.AddSingleton<ITenantRoutingRepository>(_ => new InMemoryTenantRoutingRepository(seedRoutes: []));
        services.AddSingleton<IApiKeyRepository>(_ => new InMemoryApiKeyRepository(seedKeys: []));

        services.AddSingleton(new TenantRoutingCacheOptions { Ttl = TimeSpan.FromMinutes(10) });
        services.AddSingleton<ITenantRoutingCache, TenantRoutingCache>();
        services.AddSingleton<TenantResolver>();

        services.AddSingleton(new JwksProviderOptions { Ttl = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IJwksProvider, JwksProvider>();
        services.AddSingleton<IJwtValidator, CognitoJwtValidator>();

        services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient());
        services.AddSingleton(new SaltProviderOptions
        {
            SecretId = Environment.GetEnvironmentVariable("THOR_AUTHORIZER_SALT_SECRET_ID"),
            Ttl = TimeSpan.FromMinutes(10),
        });
        services.AddSingleton<ISaltProvider, SecretsManagerSaltProvider>();

        services.AddSingleton<IApiKeyHasher, Pbkdf2ApiKeyHasher>();
        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();

        services.AddSingleton<IPolicyBuilder, IamPolicyBuilder>();
        services.AddSingleton<AuthorizerHandler>();

        return services.BuildServiceProvider();
    }
}
