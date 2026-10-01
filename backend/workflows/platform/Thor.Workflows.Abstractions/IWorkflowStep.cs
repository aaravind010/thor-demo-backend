namespace Thor.Workflows.Abstractions;

/// <summary>
/// One independently-invokable phase of a workflow module's container image. The host resolves
/// exactly one of these per invocation (by <c>THOR_STEP</c>) and passes it the raw input payload;
/// each step deserializes only the fields it needs. Most steps should derive from
/// <see cref="WorkflowStep{TRequest}"/> rather than implementing this directly.
/// </summary>
public interface IWorkflowStep
{
    /// <summary>A thrown exception is the only failure signal.</summary>
    Task<StepResult> ExecuteAsync(string inputJson, CancellationToken cancellationToken);
}
