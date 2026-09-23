using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

/// <summary>See <see cref="StagingAccountRepository"/>'s remarks — same keyless-entity, binary-COPY approach.</summary>
public sealed class StagingEntitlementRepository(TenantDbContext context) : IStagingRepository<StagingEntitlement>
{
    public async Task BulkInsertAsync(IEnumerable<StagingEntitlement> rows, CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var writer = await connection.BeginBinaryImportAsync(
                """
                COPY tenant.staging_entitlement (
                    scan_manifest_id, batch_seq, source_id, connector_type, native_id,
                    entitlement_type, name, description, is_admin, scope, instance_name,
                    sudo_path, sudo_host, raw_attributes, content_hash, received_at
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
                await writer.WriteAsync(row.EntitlementType, cancellationToken);
                await writer.WriteAsync(row.Name, cancellationToken);
                await writer.WriteAsync(row.Description, cancellationToken);
                await writer.WriteAsync(row.IsAdmin, cancellationToken);
                await writer.WriteAsync(row.Scope, cancellationToken);
                await writer.WriteAsync(row.InstanceName, cancellationToken);
                await writer.WriteAsync(row.SudoPath, cancellationToken);
                await writer.WriteAsync(row.SudoHost, cancellationToken);
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
