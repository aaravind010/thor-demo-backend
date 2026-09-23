using Amazon.StepFunctions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thor.CreateManifest.Core;
using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;

namespace Thor.CreateManifest.Function;

/// <summary>Wires Core services for the Lambda runtime.</summary>
internal static class CompositionRoot
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddLambdaLogger());

        // Both the Master and tenant legs authenticate with an RDS IAM token through the proxy
        // (ADR §6.2/§6.3) — there is no password auth path.
        var tokenProvider = new RdsIamTokenProvider();

        var masterConnectionInfo = new MasterConnectionInfo(
            Host: RequireEnv("THOR_MASTERDB_HOST"),
            Database: RequireEnv("THOR_MASTERDB_DATABASE"),
            Username: RequireEnv("THOR_MASTERDB_USER"),
            Region: RequireEnv("THOR_MASTERDB_REGION"),
            Port: int.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT"), out var masterPort) ? masterPort : 5432,
            UseSsl: bool.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_USESSL"), out var masterSsl) ? masterSsl : true);

        var masterDbContextFactory = new MasterDbContextFactory(tokenProvider);
        var routingResolver = new TenantRoutingResolver(masterDbContextFactory, masterConnectionInfo);
        var connectionValidator = new TenantConnectionValidator();
        var connectionCache = new TenantConnectionCache();
        var tenantDbContextFactory = new TenantDbContextFactory();
        var tenantConnectionManager = new TenantConnectionManager(
            routingResolver, tokenProvider, connectionValidator, connectionCache, tenantDbContextFactory);

        services.AddSingleton<ITenantRoutingResolver>(routingResolver);
        services.AddSingleton<ITenantConnectionManager>(tenantConnectionManager);
        services.AddSingleton<IScanManifestStore, TenantScanManifestStore>();

        services.AddSingleton<IAmazonStepFunctions>(_ => new AmazonStepFunctionsClient());
        services.AddSingleton(new StepFunctionsOptions(
            StateMachineArn: Environment.GetEnvironmentVariable("THOR_INGESTION_STATE_MACHINE_ARN")
                ?? throw new InvalidOperationException("THOR_INGESTION_STATE_MACHINE_ARN is not set.")));
        services.AddSingleton<IIngestionTrigger, StepFunctionsIngestionTrigger>();

        services.AddSingleton<ManifestBatchProcessor>();

        return services.BuildServiceProvider();
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
}
