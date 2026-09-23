using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Thor.DataLayer.Data;

/// <summary>
/// Lets the `dotnet ef migrations` CLI construct a <see cref="MasterDbContext"/> without a
/// running application or DI container. This is offline design-time tooling (model build /
/// `migrations add` / `script`) — it never opens a live connection, so it needs no
/// credentials. Runtime callers go through <see cref="MasterDbContextFactory"/> instead,
/// which authenticates with a short-lived RDS IAM token. Applying migrations to a live
/// database is done over that IAM runtime path (or a CI migration job), not here.
/// </summary>
public sealed class MasterDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MasterDbContext>
{
    public MasterDbContext CreateDbContext(string[] args)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("THOR_MASTERDB_HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("THOR_MASTERDB_PORT"), out var port) ? port : 5432,
            Database = Environment.GetEnvironmentVariable("THOR_MASTERDB_DATABASE") ?? "postgres",
            Username = Environment.GetEnvironmentVariable("THOR_MASTERDB_USER") ?? "postgres",
        };

        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        return new MasterDbContext(options);
    }
}
