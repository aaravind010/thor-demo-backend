namespace Thor.Workflows.Abstractions;

/// <summary>
/// One independently-invokable phase of a workflow module's container image. The host resolves
/// exactly one of these per invocation (by <c>THOR_STEP</c>) and passes it the raw
/// <c>THOR_INPUT</c> payload; each step deserializes only the fields it needs.
/// </summary>
public interface IWorkflowStep
{
    /// <summary>Returns the step's own result object (for callers that want to inspect/forward it) and whether it's still in progress (steps that never have that notion always return false); a thrown exception is the only failure signal.</summary>
    Task<(object? Result, bool IsInProgress)> ExecuteAsync(string inputJson, CancellationToken cancellationToken);
}
