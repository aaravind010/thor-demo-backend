using Microsoft.EntityFrameworkCore.Design;

namespace Thor.DataLayer.Data;

/// <summary>
/// Lets the `dotnet ef migrations` CLI construct a <see cref="TenantDbContext"/> without a
/// running application or DI container. Reads connection parameters from environment
/// variables. Not used at runtime — runtime callers go through
/// <see cref="TenantDbContextFactory"/> instead.
/// </summary>
public sealed class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var connectionInfo = new TenantConnectionInfo(
            Host: Environment.GetEnvironmentVariable("THOR_DATALAYER_HOST")!,
            Database: Environment.GetEnvironmentVariable("THOR_DATALAYER_DATABASE")!,
            Username: Environment.GetEnvironmentVariable("THOR_DATALAYER_USER")!,
            Password: Environment.GetEnvironmentVariable("THOR_DATALAYER_PASSWORD")!,
            Port: int.Parse(Environment.GetEnvironmentVariable("THOR_DATALAYER_PORT")!),
            UseSsl: false);

        return new TenantDbContextFactory().Create(connectionInfo);
    }
}
