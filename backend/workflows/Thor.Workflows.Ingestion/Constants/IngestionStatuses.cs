namespace Thor.Workflows.Ingestion.Constants;

/// <summary>
/// In-flight <c>workflow.status</c> values this module sets as it progresses through its steps,
/// via <see cref="Thor.Workflows.Abstractions.WorkflowLifecycle.MarkStatusAsync"/>. Terminal
/// states ("started"/"completed"/"failed") are set by <c>WorkflowLifecycle</c> itself.
/// </summary>
public static class IngestionStatuses
{
    public const string ExtractAndStageComplete = "extract_and_stage_complete";
    public const string PromoteComplete = "promote_complete";
    public const string GraphLoadStarted = "graph_load_started";

    public const string ExtractAndStageFailed = "extract_and_stage_failed";
    public const string PromoteFailed = "promote_failed";
    public const string GraphLoadFailed = "graph_load_failed";
}
