namespace Thor.Workflows.Ownership.Constants;

/// <summary>
/// Terminal <c>workflow.status</c> values Ownership sets beyond the generic ones
/// <c>WorkflowLifecycle</c> owns ("started"/"completed"/"failed").
/// </summary>
public static class OwnershipStatuses
{
    /// <summary>
    /// The run reached the end, but at least one chunk failed and its entities were never matched.
    /// Which windows were lost is in <c>workflow.error</c>, appended by
    /// <see cref="Steps.OwnershipRecordChunkFailureStep"/> as each one failed. The same value ATRE
    /// uses, for the same distinction.
    /// </summary>
    public const string CompletedWithErrors = "completed_with_errors";
}
