using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface IScanRepository : IRepository<Scan>
{
    /// <summary>
    /// Marks <paramref name="scanId"/> as <see cref="ScanStatus.Failed"/> — any one task failing
    /// fails the whole scan outright, regardless of its sibling tasks' state. A no-op once the
    /// scan is already terminal (<see cref="ScanStatus.Failed"/> or <see cref="ScanStatus.Completed"/>).
    /// </summary>
    Task MarkFailedAsync(Guid scanId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments <see cref="Scan.CompletedTasks"/> by one. Called once per
    /// <see cref="ScanTask"/> that reaches <see cref="ScanTaskStatus.Completed"/> — safe against
    /// double-counting because the caller only reaches here after a task-level conditional update
    /// that fires at most once per task (see <see cref="ScanTaskRepository.UpdateStatusAsync"/>).
    /// </summary>
    Task IncrementCompletedTasksAsync(Guid scanId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks <paramref name="scanId"/> as <see cref="ScanStatus.Completed"/> if — and only if —
    /// every <see cref="ScanTask"/> belonging to it is already <see cref="ScanTaskStatus.Completed"/>.
    /// A no-op once the scan is already terminal.
    /// </summary>
    Task CompleteIfAllTasksCompletedAsync(Guid scanId, CancellationToken cancellationToken = default);
}
