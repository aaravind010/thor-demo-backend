using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Thor.DataLayer.Data;

/// <summary>
/// Lets the `dotnet ef migrations` CLI construct a <see cref="TenantDbContext"/> without a
/// running application or DI container. This is offline design-time tooling (model build /
/// `migrations add` / `script`) — it never opens a live connection, so it needs no
/// credentials. Runtime callers go through <see cref="TenantDbContextFactory"/> instead.
/// Applying migrations to a live tenant database is done over the IAM runtime path (see
/// <c>PostgresTenantSchemaMigrator</c>), not here.
/// </summary>
public sealed class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("THOR_DATALAYER_HOST") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("THOR_DATALAYER_PORT"), out var port) ? port : 5432,
            Database = Environment.GetEnvironmentVariable("THOR_DATALAYER_DATABASE") ?? "postgres",
            Username = Environment.GetEnvironmentVariable("THOR_DATALAYER_USER") ?? "postgres",
        };

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(builder.ConnectionString)
            .Options;

        return new TenantDbContext(options);
    }
}
