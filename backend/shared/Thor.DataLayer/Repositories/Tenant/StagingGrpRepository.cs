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
public sealed class StagingGrpRepository(TenantDbContext context) : IStagingRepository<StagingGrp>
{
    public async Task BulkInsertAsync(IEnumerable<StagingGrp> rows, CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var writer = await connection.BeginBinaryImportAsync(
                """
                COPY tenant.staging_grp (
                    scan_manifest_id, batch_seq, tenant_id, source_id, connector_type, native_id,
                    group_class, display_name, email, domain_name,
                    is_large_group, is_deleted, raw_attributes, content_hash, received_at
                ) FROM STDIN (FORMAT BINARY)
                """,
                cancellationToken);

            foreach (var row in rows)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(row.ScanManifestId, cancellationToken);
                await writer.WriteAsync(row.BatchSeq, cancellationToken);
                await writer.WriteAsync(row.TenantId, cancellationToken);
                await writer.WriteAsync(row.SourceId, cancellationToken);
                await writer.WriteAsync(row.ConnectorType, cancellationToken);
                await writer.WriteAsync(row.NativeId, cancellationToken);
                await writer.WriteAsync(row.GroupClass, cancellationToken);
                await writer.WriteAsync(row.DisplayName, cancellationToken);
                await writer.WriteAsync(row.Email, cancellationToken);
                await writer.WriteAsync(row.DomainName, cancellationToken);
                await writer.WriteAsync(row.IsLargeGroup, cancellationToken);
                await writer.WriteAsync(row.IsDeleted, cancellationToken);
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
