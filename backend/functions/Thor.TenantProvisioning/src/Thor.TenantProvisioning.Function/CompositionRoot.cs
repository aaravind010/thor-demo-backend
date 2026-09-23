using Amazon.CognitoIdentityProvider;
using Amazon.Lambda.Logging.AspNetCore;
using Amazon.Route53;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.TenantProvisioning.Core.Abstractions;
using Thor.TenantProvisioning.Core.Models;
using Thor.TenantProvisioning.Core.Steps;
using Thor.TenantProvisioning.Function.Aws;

namespace Thor.TenantProvisioning.Function;

/// <summary>
/// Wires the provisioning steps, the Master DB repositories, and the AWS
/// (Cognito/Route53) provisioners for the Lambda runtime. One provider is shared across
/// all seven step handlers; each invocation opens its own scope so the DB context is
/// fresh per request. Config comes from environment variables — no secret values.
/// </summary>
internal static class CompositionRoot
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddLambdaLogger());
        services.AddSingleton(TimeProvider.System);

        // --- Master DB ---
        // Always RDS IAM auth: the Lambda connects as thor_provisioner with a short-lived IAM
        // token (no stored password anywhere). Host is the Aurora writer endpoint — control-
        // plane writes go direct to the cluster, not through a proxy.
        services.AddSingleton(new MasterConnectionInfo(
            Host: RequireEnv("THOR_MASTERDB_HOST"),
            Database: RequireEnv("THOR_MASTERDB_DATABASE"),
            Username: RequireEnv("THOR_MASTERDB_USER"),
            Region: RequireEnv("THOR_PROVISIONING_REGION"),
            Port: int.Parse(RequireEnv("THOR_MASTERDB_PORT")),
            UseSsl: !string.Equals(Environment.GetEnvironmentVariable("THOR_MASTERDB_SSL"), "false",
                StringComparison.OrdinalIgnoreCase)));

        services.AddSingleton<IRdsIamTokenProvider, RdsIamTokenProvider>();
        services.AddSingleton<IMasterDbContextFactory, MasterDbContextFactory>();
        services.AddScoped(sp => sp.GetRequiredService<IMasterDbContextFactory>()
            .Create(sp.GetRequiredService<MasterConnectionInfo>()));
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantRoutingRepository, TenantRoutingRepository>();

        // Endpoint (RDS Proxy) + region written into every tenant's routing row.
        services.AddSingleton(new TenantRoutingOptions(
            ClusterEndpoint: RequireEnv("THOR_PROVISIONING_ROUTING_ENDPOINT"),
            Region: RequireEnv("THOR_PROVISIONING_REGION")));

        // --- tenant database provisioning (Npgsql + IAM token; no secrets, no passwords) ---
        services.AddSingleton(new TenantDbProvisioningOptions
        {
            WriterEndpoint = RequireEnv("THOR_PROVISIONING_CLUSTER_WRITER_ENDPOINT"),
            Region = RequireEnv("THOR_PROVISIONING_REGION"),
            ProvisioningUser = Environment.GetEnvironmentVariable("THOR_PROVISIONING_DB_USER") ?? "thor_provisioner",
            AdminDatabase = Environment.GetEnvironmentVariable("THOR_PROVISIONING_ADMIN_DB") ?? "postgres",
        });
        services.AddSingleton<ITenantDatabaseProvisioner, PostgresTenantDatabaseProvisioner>();
        services.AddSingleton<ITenantSchemaMigrator, PostgresTenantSchemaMigrator>();

        // --- AWS provisioners ---
        services.AddSingleton<IAmazonCognitoIdentityProvider>(_ => new AmazonCognitoIdentityProviderClient());
        services.AddSingleton<ICognitoProvisioner, CognitoProvisioner>();

        services.AddSingleton<IAmazonRoute53>(_ => new AmazonRoute53Client());
        services.AddSingleton(new Route53Options(
            HostedZoneId: RequireEnv("THOR_PROVISIONING_HOSTED_ZONE_ID"),
            BaseDomain: RequireEnv("THOR_PROVISIONING_BASE_DOMAIN"),
            DnsTarget: RequireEnv("THOR_PROVISIONING_DNS_TARGET")));
        services.AddSingleton<ISubdomainProvisioner, Route53SubdomainProvisioner>();

        // --- steps ---
        services.AddScoped<SeedTenantMetadataStep>();
        services.AddScoped<CreateTenantDatabaseStep>();
        services.AddScoped<CreateTenantTablesStep>();
        services.AddScoped<ProvisionCognitoStep>();
        services.AddScoped<CreateAdminUserStep>();
        services.AddScoped<ConfigureSubdomainStep>();
        services.AddScoped<FinalizeRoutingStep>();

        return services.BuildServiceProvider();
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
}
