using Thor.Workflows.Abstractions;
using Thor.Workflows.Abstractions.Compute;

namespace Thor.Workflows.Hosting.Compute;

/// <summary>
/// The step that decides which compute target the rest of a run uses. Registered under
/// <c>THOR_STEP=select-compute</c> by any workflow that wants the decision made per run.
///
/// <para>Always runs on Lambda — the state machine reads its <see cref="ComputePlan"/> off the
/// invocation response, and <c>ecs:runTask.sync</c> has no channel to return one on.</para>
///
/// <para>This was Thor.Workflows.IngestionDriver, a separate zip-packaged Lambda with its own
/// project, composition root and copy of the tenant-routing factory. Nothing about deciding
/// lambda-vs-ECS was ingestion-specific except the estimate, so the estimate became an interface and
/// the rest moved here, into the same image as every other step.</para>
/// </summary>
public sealed class SelectComputeStep<TRequest>(IComputeEstimator<TRequest> estimator, SizeThresholdPolicy policy)
    : WorkflowStep<TRequest>
    where TRequest : class
{
    protected override async Task<StepResult> ExecuteAsync(TRequest request, CancellationToken cancellationToken)
    {
        var estimate = await estimator.EstimateAsync(request, cancellationToken);
        return StepResult.Completed(policy.Decide(estimate));
    }
}
