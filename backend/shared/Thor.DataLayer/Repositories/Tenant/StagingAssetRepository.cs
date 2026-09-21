using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

/// <summary>See <see cref="StagingAccountRepository"/>'s remarks — same keyless-entity, binary-COPY approach.</summary>
public sealed class StagingAssetRepository(TenantDbContext context) : IStagingRepository<StagingAsset>
{
    public async Task BulkInsertAsync(IEnumerable<StagingAsset> rows, CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var writer = await connection.BeginBinaryImportAsync(
                """
                COPY tenant.staging_asset (
                    scan_manifest_id, batch_seq, source_id, connector_type, native_id,
                    asset_type, display_name, full_path, filer_name, file_size, file_count,
                    broken_acl, is_protected, raw_attributes, content_hash, received_at
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
                await writer.WriteAsync(row.AssetType, cancellationToken);
                await writer.WriteAsync(row.DisplayName, cancellationToken);
                await writer.WriteAsync(row.FullPath, cancellationToken);
                await writer.WriteAsync(row.FilerName, cancellationToken);
                await writer.WriteAsync(row.FileSize, cancellationToken);
                await writer.WriteAsync(row.FileCount, cancellationToken);
                await writer.WriteAsync(row.BrokenAcl, cancellationToken);
                await writer.WriteAsync(row.IsProtected, cancellationToken);
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
