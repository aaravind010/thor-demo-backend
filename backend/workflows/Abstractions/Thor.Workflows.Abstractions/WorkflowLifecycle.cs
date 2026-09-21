using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Workflows.Abstractions;

/// <summary>Creates/updates a workflow module's own row in the `workflow` table.</summary>
public sealed class WorkflowLifecycle(IWorkflowRepository workflows)
{
    /// <summary>Get-or-create, idempotent. Looked up by <paramref name="runId"/> when given, else by <paramref name="scanManifestId"/>.</summary>
    public async Task<Guid> EnsureStartedAsync(
        Guid? scanId, Guid? scanManifestId, string workflowType, string trigger, Guid? runId = null, CancellationToken cancellationToken = default)
    {
        if (scanManifestId is null && runId is null)
        {
            throw new InvalidOperationException("EnsureStartedAsync requires a scanManifestId, a runId, or both.");
        }

        var existing = await FindAsync(scanManifestId, workflowType, runId, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var workflow = new WorkflowEntity
        {
            Id = Guid.NewGuid(),
            ScanId = scanId,
            ScanManifestId = scanManifestId,
            WorkflowType = workflowType,
            Trigger = trigger,
            RunId = runId,
            Status = "started",
            StartedAt = DateTimeOffset.UtcNow,
        };
        await workflows.AddAsync(workflow, cancellationToken);
        await workflows.SaveChangesAsync(cancellationToken);
        return workflow.Id;
    }

    public async Task MarkCompletedAsync(Guid? scanManifestId, string workflowType, Guid? runId = null, CancellationToken cancellationToken = default)
    {
        var workflow = await FindAsync(scanManifestId, workflowType, runId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No '{workflowType}' workflow found for {(runId is not null ? $"run '{runId}'" : $"scan manifest {scanManifestId}")}.");
        workflow.Status = "completed";
        workflow.CompletedAt = DateTimeOffset.UtcNow;
        workflows.Update(workflow);
        await workflows.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Updates status only, for in-flight progress — does not touch StartedAt/CompletedAt/Error.</summary>
    public async Task MarkStatusAsync(
        Guid? scanManifestId, string workflowType, string status, Guid? runId = null, CancellationToken cancellationToken = default)
    {
        var workflow = await FindAsync(scanManifestId, workflowType, runId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No '{workflowType}' workflow found for {(runId is not null ? $"run '{runId}'" : $"scan manifest {scanManifestId}")}.");
        workflow.Status = status;
        workflows.Update(workflow);
        await workflows.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// No-op if the row was never created (e.g. the failure happened before <see cref="EnsureStartedAsync"/> ran).
    /// <paramref name="status"/> defaults to the generic <c>"failed"</c>; callers can pass a
    /// module-specific status (e.g. naming which step failed) instead.
    /// </summary>
    public async Task MarkFailedAsync(
        Guid? scanManifestId, string workflowType, string error, Guid? runId = null, string status = "failed", CancellationToken cancellationToken = default)
    {
        var workflow = await FindAsync(scanManifestId, workflowType, runId, cancellationToken);
        if (workflow is null)
        {
            return;
        }

        workflow.Status = status;
        workflow.Error = error;
        workflow.CompletedAt = DateTimeOffset.UtcNow;
        workflows.Update(workflow);
        await workflows.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkflowEntity?> FindAsync(Guid? scanManifestId, string workflowType, Guid? runId, CancellationToken cancellationToken) =>
        runId is not null
            ? await workflows.GetByRunIdAsync(runId.Value, cancellationToken)
            : (await workflows.GetByScanManifestIdAsync(scanManifestId!.Value, cancellationToken))
                .SingleOrDefault(w => w.WorkflowType == workflowType);
}
