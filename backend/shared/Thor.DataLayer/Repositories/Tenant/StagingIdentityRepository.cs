using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

/// <summary>See <see cref="StagingAccountRepository"/>'s remarks — same keyless-entity, binary-COPY approach.</summary>
public sealed class StagingIdentityRepository(TenantDbContext context) : IStagingRepository<StagingIdentity>
{
    public async Task BulkInsertAsync(IEnumerable<StagingIdentity> rows, CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var writer = await connection.BeginBinaryImportAsync(
                """
                COPY tenant.staging_identity (
                    scan_manifest_id, batch_seq, source_id, hr_employee_id, display_name,
                    email, given_name, surname, department, title, business_unit,
                    sam_account_name, upn, manager_employee_id, manager_name,
                    ad_match_field, ad_match_value, marked_to_retire, raw_attributes,
                    content_hash, received_at
                ) FROM STDIN (FORMAT BINARY)
                """,
                cancellationToken);

            foreach (var row in rows)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(row.ScanManifestId, cancellationToken);
                await writer.WriteAsync(row.BatchSeq, cancellationToken);
                await writer.WriteAsync(row.SourceId, cancellationToken);
                await writer.WriteAsync(row.HrEmployeeId, cancellationToken);
                await writer.WriteAsync(row.DisplayName, cancellationToken);
                await writer.WriteAsync(row.Email, cancellationToken);
                await writer.WriteAsync(row.GivenName, cancellationToken);
                await writer.WriteAsync(row.Surname, cancellationToken);
                await writer.WriteAsync(row.Department, cancellationToken);
                await writer.WriteAsync(row.Title, cancellationToken);
                await writer.WriteAsync(row.BusinessUnit, cancellationToken);
                await writer.WriteAsync(row.SamAccountName, cancellationToken);
                await writer.WriteAsync(row.Upn, cancellationToken);
                await writer.WriteAsync(row.ManagerEmployeeId, cancellationToken);
                await writer.WriteAsync(row.ManagerName, cancellationToken);
                await writer.WriteAsync(row.AdMatchField, cancellationToken);
                await writer.WriteAsync(row.AdMatchValue, cancellationToken);
                await writer.WriteAsync(row.MarkedToRetire, cancellationToken);
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
