using Amazon.SecretsManager;
using Amazon.StepFunctions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Thor.CreateManifest.Core;
using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Secrets;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Data;

namespace Thor.CreateManifest.Function;

/// <summary>Wires Core services for the Lambda runtime.</summary>
internal static class CompositionRoot
{
    public static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddLambdaLogger());

        var masterConnectionInfo = new MasterConnectionInfo(
            Host: Environment.GetEnvironmentVariable("THOR_MASTERDB_HOST") ?? "localhost",
            Database: Environment.GetEnvironmentVariable("THOR_MASTERDB_DATABASE") ?? "thor_masterdb_design",
            Username: Environment.GetEnvironmentVariable("THOR_MASTERDB_USER") ?? "postgres",
            Password: Environment.GetEnvironmentVariable("THOR_MASTERDB_PASSWORD") ?? "postgres",
            Port: int.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT"), out var masterPort) ? masterPort : 5432,
            UseSsl: bool.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_USESSL"), out var masterSsl) ? masterSsl : true);

        var masterDbContextFactory = new MasterDbContextFactory();
        var routingResolver = new TenantRoutingResolver(masterDbContextFactory, masterConnectionInfo);
        var secretsManager = new AmazonSecretsManagerClient();
        var secretResolver = new TenantSecretResolver(secretsManager);
        var connectionValidator = new TenantConnectionValidator();
        var connectionCache = new TenantConnectionCache();
        var tenantDbContextFactory = new TenantDbContextFactory();
        var tenantConnectionManager = new TenantConnectionManager(
            routingResolver, secretResolver, connectionValidator, connectionCache, tenantDbContextFactory);

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
}
