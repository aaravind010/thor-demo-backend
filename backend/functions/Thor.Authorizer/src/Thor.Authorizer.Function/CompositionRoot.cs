using Amazon.Lambda.Logging.AspNetCore;
using Amazon.SecretsManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thor.Auth;
using Thor.Authorizer.Core;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.DataAccess;
using Thor.Authorizer.Core.DataAccess.Fakes;
using Thor.Authorizer.Core.Policy;
using Thor.Authorizer.Core.Tenancy;
using Thor.Authorizer.Core.Tenancy.Interface;
using Thor.Authorizer.Function.DataAccess;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;

namespace Thor.Authorizer.Function;

/// <summary>
/// Wires Core services for the Lambda runtime. Tenant routing reads the Master DB directly (in
/// VPC, RDS IAM auth via the RDS Proxy); the API-key store is still the in-memory fake (see the
/// TODO) until its schema/migration/repo land.
/// </summary>
internal static class CompositionRoot
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddLambdaLogger());
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient("CognitoJwks");

        // Tenant routing: real Master-DB lookup over a direct connection through the RDS Proxy,
        // authenticating with an RDS IAM token as the least-privilege read-only thor_authorizer
        // role (no password, no Data API). A fresh MasterDbContext is minted per call.
        services.AddSingleton(new MasterConnectionInfo(
            Host: RequireEnv("THOR_MASTERDB_HOST"),
            Database: RequireEnv("THOR_MASTERDB_DATABASE"),
            Username: RequireEnv("THOR_MASTERDB_USER"),
            Region: RequireEnv("THOR_MASTERDB_REGION")));
        services.AddSingleton<IRdsIamTokenProvider, RdsIamTokenProvider>();
        services.AddSingleton<IMasterDbContextFactory, MasterDbContextFactory>();
        services.AddSingleton<ITenantRoutingRepository, MasterDbTenantRoutingRepository>();

        // TODO: API keys still use the in-memory fake — no api_keys store exists yet (separate
        // ticket: schema + migration + repo). Empty seed data; the api-key path is not live.
        services.AddSingleton<IApiKeyRepository>(_ => new InMemoryApiKeyRepository(seedKeys: []));

        services.AddSingleton(new TenantRoutingCacheOptions { Ttl = TimeSpan.FromMinutes(10) });
        services.AddSingleton<ITenantRoutingCache, TenantRoutingCache>();
        services.AddSingleton<TenantResolver>();

        services.AddSingleton(new JwksProviderOptions { Ttl = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IJwksProvider, CognitoJwksProvider>();
        services.AddSingleton<ICognitoValidator, CognitoJwtValidator>();

        // RSA public key (RS256) matching the private key Thor.Api's connector registration flow
        // signs with (ADR §5.2/§8) — same key TaskApi's ThorTokenValidator verifies against, so a
        // connector JWT presented to the authorizer validates identically wherever it lands.
        services.AddSingleton<ITokenValidator>(_ => new ThorTokenValidator(
            Environment.GetEnvironmentVariable("THOR_TASKAPI_JWT_PUBLIC_KEY")!));

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

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
}
