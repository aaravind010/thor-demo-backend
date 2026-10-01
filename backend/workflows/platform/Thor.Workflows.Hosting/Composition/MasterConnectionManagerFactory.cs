using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;

namespace Thor.Workflows.Hosting.Composition;

/// <summary>
/// Builds Master-DB access (ADR §6.2) from <c>THOR_MASTERDB_*</c> env vars, falling back to local
/// Postgres for dev — no tenant routing, just a connection to the Master metadata DB itself.
/// Authenticates with an RDS IAM token minted for <c>THOR_MASTERDB_USER</c> in
/// <c>THOR_MASTERDB_REGION</c>; there is no password auth path.
///
/// Shared by any composition root that only needs Master-DB metadata lookups (e.g. the ingestion
/// module's connector-type catalog) as well as <see cref="TenantConnectionManagerFactory"/>, which
/// layers tenant routing on top of this same connection.
/// </summary>
public static class MasterConnectionManagerFactory
{
    public static (IMasterDbContextFactory Factory, MasterConnectionInfo ConnectionInfo) Build() =>
        Build(new RdsIamTokenProvider());

    internal static (IMasterDbContextFactory Factory, MasterConnectionInfo ConnectionInfo) Build(IRdsIamTokenProvider tokenProvider)
    {
        var masterConnectionInfo = new MasterConnectionInfo(
            Host: Environment.GetEnvironmentVariable("THOR_MASTERDB_HOST") ?? "localhost",
            Database: Environment.GetEnvironmentVariable("THOR_MASTERDB_DATABASE") ?? "thor_masterdb_design",
            Username: Environment.GetEnvironmentVariable("THOR_MASTERDB_USER") ?? "postgres",
            Region: Environment.GetEnvironmentVariable("THOR_MASTERDB_REGION")
                ?? throw new InvalidOperationException("THOR_MASTERDB_REGION is not set."),
            Port: int.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT"), out var masterPort) ? masterPort : 5432,
            UseSsl: bool.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_USESSL"), out var masterSsl) ? masterSsl : true);

        return (new MasterDbContextFactory(tokenProvider), masterConnectionInfo);
    }
}
