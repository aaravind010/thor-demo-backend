using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Secrets;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Data;
using Amazon.SecretsManager;

namespace Thor.Workflows.IngestionDriver.Core.Composition;

/// <summary>
/// Builds the Master-DB tenant-routing chain (ADR §6.2/§6.3) from <c>THOR_MASTERDB_*</c> env
/// vars, falling back to local Postgres for dev — same wiring as
/// <c>Thor.Workflows.Ingestion.Composition.TenantConnectionManagerFactory</c>.
/// </summary>
public static class TenantConnectionManagerFactory
{
    public static ITenantConnectionManager Build()
    {
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
        return new TenantConnectionManager(
            routingResolver, secretResolver, connectionValidator, connectionCache, tenantDbContextFactory,
            new TenantConnectionOptions());
    }
}
