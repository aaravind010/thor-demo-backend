namespace Thor.Workflows.Abstractions.Compute;

/// <summary>
/// A workflow's answer to "how big is this run?", used by <c>SelectComputeStep</c> to pick a compute
/// target. Implement it only if the workflow wants the decision made per run; a workflow that always
/// wants one target simply does not register a select-compute step, and its Terraform step list
/// declares that target directly.
/// </summary>
public interface IComputeEstimator<in TRequest>
    where TRequest : class
{
    Task<ComputeEstimate> EstimateAsync(TRequest request, CancellationToken cancellationToken);
}
