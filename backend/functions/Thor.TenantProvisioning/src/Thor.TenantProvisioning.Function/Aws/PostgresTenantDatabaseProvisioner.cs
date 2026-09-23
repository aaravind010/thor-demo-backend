using System.Text.RegularExpressions;
using Amazon;
using Amazon.RDS.Util;
using Microsoft.Extensions.Logging;
using Npgsql;
using Thor.TenantProvisioning.Core.Abstractions;

namespace Thor.TenantProvisioning.Function.Aws;

/// <summary>
/// Creates the tenant database and its <c>_rw</c>/<c>_ro</c> IAM roles via Npgsql. The roles
/// are login roles with <c>GRANT rds_iam</c> and <b>no password</b> — auth is end-to-end IAM
/// through the tenant RDS Proxy, so nothing but role names leaves this step. The admin
/// connection itself authenticates with a short-lived RDS IAM token (no stored admin
/// password). Every operation is idempotent so Step Functions retries replay safely.
/// </summary>
public sealed partial class PostgresTenantDatabaseProvisioner(
    TenantDbProvisioningOptions options,
    ILogger<PostgresTenantDatabaseProvisioner> logger)
    : ITenantDatabaseProvisioner
{
    public async Task<TenantDatabaseResult> ProvisionAsync(
        TenantDatabaseRequest request, CancellationToken cancellationToken = default)
    {
        var databaseName = Identifier($"tenant_{Sanitize(request.Subdomain)}");
        var idN = request.TenantId.ToString("N");
        var rwRole = Identifier($"tenant_{idN}_rw");
        var roRole = Identifier($"tenant_{idN}_ro");

        // Admin work on the maintenance DB: create the IAM roles + the database (owned by rw).
        // Schema tenant doesn't exist yet at this point (the CreateTenantTables step creates
        // it via EF Core migrations run as rw) — ro's grants into that schema are issued there,
        // once it exists, not here.
        await using (var admin = await OpenAsync(options.AdminDatabase, cancellationToken))
        {
            await EnsureIamRoleAsync(admin, rwRole, cancellationToken);
            await EnsureIamRoleAsync(admin, roRole, cancellationToken);
            await EnsureDatabaseAsync(admin, databaseName, rwRole, cancellationToken);
            await ExecuteAsync(admin, $"GRANT CONNECT ON DATABASE \"{databaseName}\" TO \"{roRole}\";", cancellationToken);
        }

        logger.LogInformation("Provisioned database {DatabaseName} (rw={Rw}, ro={Ro}).", databaseName, rwRole, roRole);
        return new TenantDatabaseResult(databaseName, rwRole, roRole);
    }

    private async Task<NpgsqlConnection> OpenAsync(string database, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.WriterEndpoint,
            Port = options.Port,
            Database = database,
            Username = options.ProvisioningUser,
            Password = RDSAuthTokenGenerator.GenerateAuthToken(
                RegionEndpoint.GetBySystemName(options.Region), options.WriterEndpoint, options.Port, options.ProvisioningUser),
            SslMode = SslMode.Require,
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // Creates the role as a passwordless login role, then grants rds_iam so it authenticates
    // via IAM tokens. The rds_iam grant is skipped when that role is absent (local Postgres),
    // which keeps the step runnable off-Aurora for testing.
    private static async Task EnsureIamRoleAsync(
        NpgsqlConnection connection, string role, CancellationToken cancellationToken)
    {
        var exists = await ScalarExistsAsync(connection, "SELECT 1 FROM pg_roles WHERE rolname = @name", role, cancellationToken);
        if (!exists)
        {
            await ExecuteAsync(connection, $"CREATE ROLE \"{role}\" WITH LOGIN;", cancellationToken);
        }

        // PG16+: CREATEROLE gives the creator ADMIN on the new role but not SET, and both
        // CREATE DATABASE ... OWNER and SET ROLE below need SET. The creator holds ADMIN, so it
        // can grant itself membership; re-running on retry just re-applies the option.
        await ExecuteAsync(connection, $"GRANT \"{role}\" TO CURRENT_USER WITH SET TRUE;", cancellationToken);

        var rdsIamExists = await ScalarExistsAsync(connection, "SELECT 1 FROM pg_roles WHERE rolname = @name", "rds_iam", cancellationToken);
        if (rdsIamExists)
        {
            await ExecuteAsync(connection, $"GRANT rds_iam TO \"{role}\";", cancellationToken);
        }
    }

    private static async Task EnsureDatabaseAsync(
        NpgsqlConnection connection, string database, string ownerRole, CancellationToken cancellationToken)
    {
        var exists = await ScalarExistsAsync(connection, "SELECT 1 FROM pg_database WHERE datname = @name", database, cancellationToken);
        if (!exists)
        {
            // CREATE DATABASE cannot run in a transaction block; Npgsql auto-commits a lone command.
            await ExecuteAsync(connection, $"CREATE DATABASE \"{database}\" OWNER \"{ownerRole}\";", cancellationToken);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ScalarExistsAsync(
        NpgsqlConnection connection, string sql, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static string Sanitize(string subdomain) =>
        NonIdentifierChars().Replace(subdomain.ToLowerInvariant(), "_");

    private static string Identifier(string value)
    {
        if (!IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a safe PostgreSQL identifier.", nameof(value));
        }

        return value;
    }

    [GeneratedRegex("[^a-z0-9_]")]
    private static partial Regex NonIdentifierChars();

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierPattern();
}
