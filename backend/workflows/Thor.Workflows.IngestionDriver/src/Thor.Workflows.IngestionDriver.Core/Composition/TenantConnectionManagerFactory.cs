using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;

namespace Thor.Workflows.IngestionDriver.Core.Composition;

/// <summary>
/// Builds the Master-DB tenant-routing chain (ADR §6.2/§6.3) from <c>THOR_MASTERDB_*</c> env
/// vars, falling back to local Postgres for dev — same wiring as
/// <c>Thor.Workflows.Ingestion.Composition.TenantConnectionManagerFactory</c>. Both the Master
/// and tenant legs authenticate with an RDS IAM token minted for <c>THOR_MASTERDB_USER</c> in
/// <c>THOR_MASTERDB_REGION</c>; there is no password auth path.
/// </summary>
public static class TenantConnectionManagerFactory
{
    public static ITenantConnectionManager Build()
    {
        var tokenProvider = new RdsIamTokenProvider();

        var masterConnectionInfo = new MasterConnectionInfo(
            Host: Environment.GetEnvironmentVariable("THOR_MASTERDB_HOST") ?? "localhost",
            Database: Environment.GetEnvironmentVariable("THOR_MASTERDB_DATABASE") ?? "thor_masterdb_design",
            Username: Environment.GetEnvironmentVariable("THOR_MASTERDB_USER") ?? "postgres",
            Region: Environment.GetEnvironmentVariable("THOR_MASTERDB_REGION")
                ?? throw new InvalidOperationException("THOR_MASTERDB_REGION is not set."),
            Port: int.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT"), out var masterPort) ? masterPort : 5432,
            UseSsl: bool.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_USESSL"), out var masterSsl) ? masterSsl : true);

        var masterDbContextFactory = new MasterDbContextFactory(tokenProvider);
        var routingResolver = new TenantRoutingResolver(masterDbContextFactory, masterConnectionInfo);
        var connectionValidator = new TenantConnectionValidator();
        var connectionCache = new TenantConnectionCache();
        var tenantDbContextFactory = new TenantDbContextFactory();
        return new TenantConnectionManager(
            routingResolver, tokenProvider, connectionValidator, connectionCache, tenantDbContextFactory);
    }
}
