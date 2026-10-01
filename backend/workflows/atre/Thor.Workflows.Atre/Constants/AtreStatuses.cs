namespace Thor.Workflows.Atre.Constants;

/// <summary>
/// Terminal <c>workflow.status</c> values ATRE sets beyond the generic ones
/// <c>WorkflowLifecycle</c> owns ("started"/"completed"/"failed").
/// </summary>
public static class AtreStatuses
{
    /// <summary>
    /// The run reached the end, but at least one chunk failed and its accounts were never classified.
    /// A distinct status because "nothing was classified" and "most of it was, these windows were
    /// missed" need different responses, and <c>failed</c> alone cannot tell them apart. Which windows
    /// were lost is in <c>workflow.error</c>, appended by
    /// <see cref="Steps.AtreRecordChunkFailureStep"/> as each one failed.
    /// </summary>
    public const string CompletedWithErrors = "completed_with_errors";
}
