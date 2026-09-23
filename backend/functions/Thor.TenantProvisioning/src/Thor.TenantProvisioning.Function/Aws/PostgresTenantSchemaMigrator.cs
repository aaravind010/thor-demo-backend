using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;
using Thor.TenantProvisioning.Core.Abstractions;

namespace Thor.TenantProvisioning.Function.Aws;

/// <summary>
/// Applies <c>TenantDbContext</c>'s EF Core migrations, authenticating directly as the
/// tenant's own <c>_rw</c> IAM role (a short-lived RDS IAM token — no admin hop, no stored
/// password) so the tables migrations create are owned by <c>rw</c>. Builds its own
/// <see cref="DbContextOptionsBuilder{TContext}"/> rather than going through
/// <see cref="ITenantDbContextFactory"/>, which doesn't expose migrations-history-table
/// configuration — the same pattern <c>Thor.DbBootstrap.Function</c> uses for the Master DB.
/// Once migrations have created schema <c>tenant</c>, grants <c>_ro</c> visibility into it:
/// existing tables get an explicit <c>SELECT</c>, and a default-privilege grant covers tables
/// created by later (blue/green) migrations.
/// </summary>
public sealed class PostgresTenantSchemaMigrator(
    TenantDbProvisioningOptions options,
    IRdsIamTokenProvider tokenProvider,
    ILogger<PostgresTenantSchemaMigrator> logger) : ITenantSchemaMigrator
{
    public async Task MigrateAsync(TenantSchemaMigrationRequest request, CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString(request.DatabaseName, request.RwDbUser);

        var dbOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tenant"))
            .Options;

        await using (var db = new TenantDbContext(dbOptions))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, $"GRANT USAGE ON SCHEMA tenant TO \"{request.RoDbUser}\";", cancellationToken);
        await ExecuteAsync(connection, $"GRANT SELECT ON ALL TABLES IN SCHEMA tenant TO \"{request.RoDbUser}\";", cancellationToken);
        await ExecuteAsync(connection,
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA tenant GRANT SELECT ON TABLES TO \"{request.RoDbUser}\";", cancellationToken);

        logger.LogInformation("Migrated {DatabaseName} and granted {RoDbUser} read access to schema tenant.",
            request.DatabaseName, request.RoDbUser);
    }

    private string BuildConnectionString(string database, string rwDbUser) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = options.WriterEndpoint,
            Port = options.Port,
            Database = database,
            Username = rwDbUser,
            Password = tokenProvider.GenerateToken(options.WriterEndpoint, options.Port, rwDbUser, options.Region),
            SslMode = SslMode.Require,
        }.ConnectionString;

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
