using System.Text;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

/// <summary>
/// Every write here is an upsert on the (scan_id, file_location) unique index rather than a
/// read-then-write, because nothing that calls it runs exactly once: Step Functions retries each
/// Map item, the trigger queue delivers at least once, and the per-file Map items run concurrently
/// with each other.
/// </summary>
public sealed class ScanFileRepository(TenantDbContext context)
    : Repository<ScanFile>(context), IScanFileRepository
{
    public async Task MarkReceivedAsync(IReadOnlyList<ScanFile> files, CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return;
        }

        var sql = new StringBuilder("""
            INSERT INTO tenant.scan_file (id, scan_id, scan_task_id, file_location, status, error, created_at, updated_at)
            VALUES

            """);
        var parameters = new List<NpgsqlParameter>();

        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            if (i > 0)
            {
                sql.Append(",\n");
            }

            sql.Append($"(@id{i}, @scan_id{i}, @scan_task_id{i}, @file_location{i}, @status{i}, NULL, @created_at{i}, @updated_at{i})");

            parameters.Add(new NpgsqlParameter($"id{i}", f.Id));
            parameters.Add(new NpgsqlParameter($"scan_id{i}", f.ScanId));
            parameters.Add(new NpgsqlParameter($"scan_task_id{i}", f.ScanTaskId));
            parameters.Add(new NpgsqlParameter($"file_location{i}", f.FileLocation));
            parameters.Add(new NpgsqlParameter($"status{i}", f.Status));
            parameters.Add(new NpgsqlParameter($"created_at{i}", f.CreatedAt));
            parameters.Add(new NpgsqlParameter($"updated_at{i}", f.UpdatedAt));
        }

        sql.Append("\nON CONFLICT (scan_id, file_location) DO NOTHING");

        await TenantSqlExec.ExecAsync(context, sql.ToString(), parameters, cancellationToken);
    }

    public async Task MarkStatusAsync(ScanFile file, CancellationToken cancellationToken = default)
    {
        const string Sql = """
            INSERT INTO tenant.scan_file (id, scan_id, scan_task_id, file_location, status, error, created_at, updated_at)
            VALUES (@id, @scan_id, @scan_task_id, @file_location, @status, @error, @created_at, @updated_at)
            ON CONFLICT (scan_id, file_location) DO UPDATE
            SET status = EXCLUDED.status, error = EXCLUDED.error, updated_at = EXCLUDED.updated_at
            """;

        List<NpgsqlParameter> parameters =
        [
            new("id", file.Id),
            new("scan_id", file.ScanId),
            new("scan_task_id", file.ScanTaskId),
            new("file_location", file.FileLocation),
            new("status", file.Status),
            new("error", (object?)file.Error ?? DBNull.Value),
            new("created_at", file.CreatedAt),
            new("updated_at", file.UpdatedAt),
        ];

        await TenantSqlExec.ExecAsync(context, Sql, parameters, cancellationToken);
    }
}
