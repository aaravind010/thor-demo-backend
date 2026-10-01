using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class ScanTaskRepository(TenantDbContext context)
    : Repository<ScanTask>(context), IScanTaskRepository
{
    public async Task<IReadOnlyList<ScanTask>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default) =>
        await context.Tasks.Where(t => t.ScanId == scanId).ToListAsync(cancellationToken);

    // Before selecting candidates, reclaims InProgress tasks whose last heartbeat (see
    // RefreshHeartbeatAsync) is older than stallTimeout: back to Pending with RetryCount
    // incremented, or straight to Dead if that would exceed maxRetries. LastHeartbeatAt (not
    // StartedAt) drives this so a task that's still being legitimately worked — just slower
    // than expected — isn't reclaimed out from under its connector as long as it keeps
    // heartbeating.
    public async Task<IReadOnlyList<ScanTask>> ClaimPendingTasksAsync(
        int limit, TimeSpan stallTimeout, int maxRetries, CancellationToken cancellationToken = default)
    {
        var stallThreshold = DateTimeOffset.UtcNow - stallTimeout;

        await context.Tasks
            .Where(t => t.Status == ScanTaskStatus.InProgress && t.LastHeartbeatAt < stallThreshold)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, t => t.RetryCount + 1 > maxRetries ? ScanTaskStatus.Dead : ScanTaskStatus.Pending)
                .SetProperty(t => t.RetryCount, t => t.RetryCount + 1), cancellationToken);

        var candidateIds = await context.Tasks
            .Where(t => t.Status == ScanTaskStatus.Pending)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Take(limit)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            return [];
        }

        // Stamped on every row this call actually claims, so the final read below can tell
        // "claimed by me just now" apart from "already InProgress from someone else". Also seeds
        // LastHeartbeatAt so a freshly claimed task isn't already stale before its first
        // heartbeat call arrives.
        var claimedAt = DateTimeOffset.UtcNow;

        await context.Tasks
            .Where(t => candidateIds.Contains(t.Id) && t.Status == ScanTaskStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, ScanTaskStatus.InProgress)
                .SetProperty(t => t.StartedAt, claimedAt)
                .SetProperty(t => t.LastHeartbeatAt, claimedAt), cancellationToken);

        return await context.Tasks
            .Where(t => candidateIds.Contains(t.Id) && t.StartedAt == claimedAt)
            .Include(t => t.Scan).ThenInclude(s => s.ScanConfig)
            .Include(t => t.Source)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    // A single conditional UPDATE, same pattern as the claim above: only refreshes
    // LastHeartbeatAt if the task is still InProgress, so a heartbeat that loses the race
    // against the stall sweep (task already reclaimed) affects 0 rows instead of resurrecting a
    // claim that's no longer this caller's. The follow-up existence check only runs when the
    // update affected nothing, so it costs nothing on the (expected) common path.
    public async Task<ScanTaskHeartbeatResult> RefreshHeartbeatAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await context.Tasks
            .Where(t => t.Id == taskId && t.Status == ScanTaskStatus.InProgress)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastHeartbeatAt, DateTimeOffset.UtcNow), cancellationToken);

        if (rowsAffected > 0)
        {
            return ScanTaskHeartbeatResult.Refreshed;
        }

        var exists = await context.Tasks.AnyAsync(t => t.Id == taskId, cancellationToken);
        return exists ? ScanTaskHeartbeatResult.NotInProgress : ScanTaskHeartbeatResult.NotFound;
    }

    // Same conditional-UPDATE pattern as RefreshHeartbeatAsync: only finalizes the task if it's
    // still InProgress, so a status report that loses the race against the stall sweep affects 0
    // rows instead of overwriting a claim that's no longer this caller's. Looks up ScanId
    // up front (rather than only on failure, as the heartbeat/claim methods do) because the
    // caller needs it on success to roll the outcome up into the parent Scan. CompletedAt is only
    // stamped for a terminal status — releasing a task back to Pending isn't a completion, and
    // leaves it eligible to be claimed again by ClaimPendingTasksAsync.
    public async Task<(ScanTaskStatusUpdateResult Result, Guid ScanId)> UpdateStatusAsync(Guid taskId, string status, CancellationToken cancellationToken = default)
    {
        var scanId = await context.Tasks
            .Where(t => t.Id == taskId)
            .Select(t => (Guid?)t.ScanId)
            .FirstOrDefaultAsync(cancellationToken);

        if (scanId is null)
        {
            return (ScanTaskStatusUpdateResult.NotFound, Guid.Empty);
        }

        var isReleaseToPending = status == ScanTaskStatus.Pending;
        var now = DateTimeOffset.UtcNow;

        var rowsAffected = await context.Tasks
            .Where(t => t.Id == taskId && t.Status == ScanTaskStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, status)
                .SetProperty(t => t.CompletedAt, t => isReleaseToPending ? t.CompletedAt : now), cancellationToken);

        return rowsAffected > 0
            ? (ScanTaskStatusUpdateResult.Updated, scanId.Value)
            : (ScanTaskStatusUpdateResult.NotInProgress, Guid.Empty);
    }

    public async Task<ScanTask?> GetByIdWithSettingsAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        await context.Tasks
            .Where(t => t.Id == taskId)
            .Include(t => t.Source)
            .Include(t => t.Scan).ThenInclude(s => s.ScanConfig).ThenInclude(c => c.ConnectorConfigValues)
            .Include(t => t.Scan).ThenInclude(s => s.ScanConfig).ThenInclude(c => c.AuthenticationMethod).ThenInclude(m => m.AuthenticationValues)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
}
