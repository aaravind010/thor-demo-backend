using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class WorkflowRepository(TenantDbContext context)
    : Repository<WorkflowEntity>(context), IWorkflowRepository
{
    public async Task<IReadOnlyList<WorkflowEntity>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default) =>
        await context.Workflows.Where(w => w.ScanId == scanId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowEntity>> GetByScanManifestIdAsync(Guid scanManifestId, CancellationToken cancellationToken = default) =>
        await context.Workflows.Where(w => w.ScanManifestId == scanManifestId).ToListAsync(cancellationToken);

    public async Task<WorkflowEntity?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await context.Workflows.SingleOrDefaultAsync(w => w.RunId == runId, cancellationToken);

    public async Task AppendErrorAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        const string Sql = """
            UPDATE tenant.workflow
            SET error = CASE
                    WHEN error IS NULL OR error = '' THEN @error
                    ELSE error || chr(10) || @error
                END
            WHERE id = @id
            """;

        List<NpgsqlParameter> parameters = [new("id", id), new("error", error)];

        await TenantSqlExec.ExecAsync(context, Sql, parameters, cancellationToken);
    }

    public async Task ReopenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string Sql = """
            UPDATE tenant.workflow
            SET status = 'started', error = NULL, completed_at = NULL
            WHERE id = @id
            """;

        await TenantSqlExec.ExecAsync(context, Sql, [new NpgsqlParameter("id", id)], cancellationToken);
    }
}
