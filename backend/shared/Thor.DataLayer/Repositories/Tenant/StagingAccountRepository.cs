using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

/// <summary>
/// EF Core's <c>HasNoKey()</c> entities (see <see cref="TenantDbContext.OnModelCreating"/>)
/// are read-only — <c>Add</c>/<c>SaveChanges</c> silently performs no write. This uses
/// Npgsql's binary <c>COPY</c> import directly instead, the .NET equivalent of the POC's
/// <c>psycopg2.extras.execute_values</c> bulk insert.
/// </summary>
public sealed class StagingAccountRepository(TenantDbContext context) : IStagingRepository<StagingAccount>
{
    public async Task BulkInsertAsync(IEnumerable<StagingAccount> rows, CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var writer = await connection.BeginBinaryImportAsync(
                """
                COPY tenant.staging_account (
                    scan_manifest_id, batch_seq, source_id, connector_type, native_id,
                    account_kind, is_human, display_name, sam_account_name,
                    upn, email, domain_name, filer_name, native_account_id,
                    is_deleted, is_disabled, raw_attributes, content_hash, received_at
                ) FROM STDIN (FORMAT BINARY)
                """,
                cancellationToken);

            foreach (var row in rows)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(row.ScanManifestId, cancellationToken);
                await writer.WriteAsync(row.BatchSeq, cancellationToken);
                await writer.WriteAsync(row.SourceId, cancellationToken);
                await writer.WriteAsync(row.ConnectorType, cancellationToken);
                await writer.WriteAsync(row.NativeId, cancellationToken);
                await writer.WriteAsync(row.AccountKind, cancellationToken);
                await writer.WriteAsync(row.IsHuman, cancellationToken);
                await writer.WriteAsync(row.DisplayName, cancellationToken);
                await writer.WriteAsync(row.SamAccountName, cancellationToken);
                await writer.WriteAsync(row.Upn, cancellationToken);
                await writer.WriteAsync(row.Email, cancellationToken);
                await writer.WriteAsync(row.DomainName, cancellationToken);
                await writer.WriteAsync(row.FilerName, cancellationToken);
                await writer.WriteAsync(row.NativeAccountId, cancellationToken);
                await writer.WriteAsync(row.IsDeleted, cancellationToken);
                await writer.WriteAsync(row.IsDisabled, cancellationToken);
                await writer.WriteAsync(row.RawAttributes, cancellationToken);
                await writer.WriteAsync(row.ContentHash, cancellationToken);
                await writer.WriteAsync(row.ReceivedAt, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
