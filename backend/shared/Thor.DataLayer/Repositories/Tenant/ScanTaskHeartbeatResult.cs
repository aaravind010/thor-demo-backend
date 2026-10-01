namespace Thor.DataLayer.Repositories;

/// <summary>Outcome of <see cref="IScanTaskRepository.RefreshHeartbeatAsync"/>.</summary>
public enum ScanTaskHeartbeatResult
{
    /// <summary>The task exists, was InProgress, and its heartbeat was refreshed.</summary>
    Refreshed,

    /// <summary>The task exists but is no longer InProgress — its claim was likely reclaimed.</summary>
    NotInProgress,

    /// <summary>No task with that id exists.</summary>
    NotFound,
}
