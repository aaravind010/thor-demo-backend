using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IScanTaskRepository : IRepository<ScanTask>
{
    Task<IReadOnlyList<ScanTask>> GetByScanIdAsync(Guid scanId, CancellationToken cancellationToken = default);

    /// <summary>
    /// First reclaims tasks whose last heartbeat (see <see cref="RefreshHeartbeatAsync"/>) is
    /// older than <paramref name="stallTimeout"/> (back to <see cref="ScanTaskStatus.Pending"/>,
    /// or to <see cref="ScanTaskStatus.Dead"/> once <paramref name="maxRetries"/> would be
    /// exceeded), then claims up to <paramref name="limit"/> pending tasks (oldest first) by
    /// flipping them to <see cref="ScanTaskStatus.InProgress"/>, so concurrent callers never
    /// receive the same task twice, and returns them with <see cref="ScanTask.Scan"/> (and its
    /// <see cref="Scan.ScanConfig"/>) and <see cref="ScanTask.Source"/> eagerly loaded for
    /// building connector-facing responses. See implementation for the concurrency strategy.
    /// </summary>
    Task<IReadOnlyList<ScanTask>> ClaimPendingTasksAsync(
        int limit, TimeSpan stallTimeout, int maxRetries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes <see cref="ScanTask.LastHeartbeatAt"/> for <paramref name="taskId"/> if — and
    /// only if — it is still <see cref="ScanTaskStatus.InProgress"/>, so a heartbeat that loses
    /// the race against a stall-reclaim (see <see cref="ClaimPendingTasksAsync"/>) doesn't
    /// resurrect a claim that's no longer the caller's.
    /// </summary>
    Task<ScanTaskHeartbeatResult> RefreshHeartbeatAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves <paramref name="taskId"/> to <paramref name="status"/> (expected to be one of
    /// <see cref="ScanTaskStatus.ConnectorSettableStatuses"/>) if — and only if — it is still
    /// <see cref="ScanTaskStatus.InProgress"/>, so a status report that loses the race against a
    /// stall-reclaim (see <see cref="ClaimPendingTasksAsync"/>) doesn't resurrect a claim that's
    /// no longer the caller's. Stamps <see cref="ScanTask.CompletedAt"/> for a terminal status
    /// (<see cref="ScanTaskStatus.Completed"/>/<see cref="ScanTaskStatus.Failed"/>) but not for
    /// <see cref="ScanTaskStatus.Pending"/>, which just releases the task back to the queue.
    /// ScanId (the task's parent scan) is only meaningful when
    /// <see cref="ScanTaskStatusUpdateResult.Updated"/> is returned; it lets the caller roll the
    /// outcome up into the parent <see cref="Scan"/>.
    /// </summary>
    Task<(ScanTaskStatusUpdateResult Result, Guid ScanId)> UpdateStatusAsync(Guid taskId, string status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Backs <c>GET /tasks/{taskId}/settings</c>: looks up <paramref name="taskId"/> with
    /// <see cref="ScanTask.Source"/>, <see cref="ScanTask.Scan"/>, its
    /// <see cref="Scan.ScanConfig"/>, and that config's <see cref="ScanConfig.ConnectorConfigValues"/>
    /// and <see cref="ScanConfig.AuthenticationMethod"/> (with
    /// <see cref="AuthenticationMethod.AuthenticationValues"/>) eagerly loaded, so the caller can
    /// build the connector settings/auth-method response without further round trips.
    /// </summary>
    Task<ScanTask?> GetByIdWithSettingsAsync(Guid taskId, CancellationToken cancellationToken = default);
}
