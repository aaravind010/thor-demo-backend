namespace Thor.DataLayer.Repositories;

/// <summary>Outcome of <see cref="IScanTaskRepository.UpdateStatusAsync"/>.</summary>
public enum ScanTaskStatusUpdateResult
{
    /// <summary>The task exists, was InProgress, and its status was updated.</summary>
    Updated,

    /// <summary>The task exists but is no longer InProgress — its claim was likely reclaimed.</summary>
    NotInProgress,

    /// <summary>No task with that id exists.</summary>
    NotFound,
}
