using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;

namespace Thor.Workflows.Hosting.Composition;

/// <summary>
/// Builds the Master-DB tenant-routing chain (ADR §6.2/§6.3) on top of
/// <see cref="MasterConnectionManagerFactory"/> — shared by every step's composition root, since
/// each step resolves a tenant's own DB the same way; only what it does with it differs.
///
/// Lives here rather than in a workflow module because every module needs exactly this chain. It was
/// previously duplicated byte-for-byte between Thor.Workflows.Ingestion and
/// Thor.Workflows.IngestionDriver, which had no shared assembly they could both reference.
/// </summary>
public static class TenantConnectionManagerFactory
{
    public static ITenantConnectionManager Build()
    {
        var tokenProvider = new RdsIamTokenProvider();
        var (masterDbContextFactory, masterConnectionInfo) = MasterConnectionManagerFactory.Build(tokenProvider);

        var routingResolver = new TenantRoutingResolver(masterDbContextFactory, masterConnectionInfo);
        var connectionValidator = new TenantConnectionValidator();
        var connectionCache = new TenantConnectionCache();
        var tenantDbContextFactory = new TenantDbContextFactory();
        return new TenantConnectionManager(
            routingResolver, tokenProvider, connectionValidator, connectionCache, tenantDbContextFactory);
    }
}
