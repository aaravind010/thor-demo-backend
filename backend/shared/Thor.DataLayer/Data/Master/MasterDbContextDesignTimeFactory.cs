using Microsoft.EntityFrameworkCore.Design;

namespace Thor.DataLayer.Data;

/// <summary>
/// Lets the `dotnet ef migrations` CLI construct a <see cref="MasterDbContext"/> without a
/// running application or DI container. Reads connection parameters from environment
/// variables. Not used at runtime — runtime callers go through
/// <see cref="MasterDbContextFactory"/> instead.
/// </summary>
public sealed class MasterDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MasterDbContext>
{
    public MasterDbContext CreateDbContext(string[] args)
    {
        var connectionInfo = new MasterConnectionInfo(
            Host: Environment.GetEnvironmentVariable("THOR_MASTERDB_HOST")!,
            Database: Environment.GetEnvironmentVariable("THOR_MASTERDB_DATABASE")!,
            Username: Environment.GetEnvironmentVariable("THOR_MASTERDB_USER")!,
            Password: Environment.GetEnvironmentVariable("THOR_MASTERDB_PASSWORD")!,
            Port: int.Parse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT")!),
            UseSsl: false);

        return new MasterDbContextFactory().Create(connectionInfo);
    }
}
