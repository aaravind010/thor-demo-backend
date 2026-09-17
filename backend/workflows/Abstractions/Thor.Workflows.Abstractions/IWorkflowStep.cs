namespace Thor.Workflows.Abstractions;

/// <summary>
/// One independently-invokable phase of a workflow module's container image. The host resolves
/// exactly one of these per invocation (by <c>THOR_STEP</c>) and passes it the raw
/// <c>THOR_INPUT</c> payload; each step deserializes only the fields it needs.
/// </summary>
public interface IWorkflowStep
{
    Task ExecuteAsync(string inputJson, CancellationToken cancellationToken);
}
