using Microsoft.Extensions.Logging;
using Thor.Workflows.Abstractions;

namespace Thor.Workflows.Hosting;

/// <summary>
/// Container host for the ECS/Fargate compute target: reads which step to run and its input from
/// environment variables, resolves the matching <see cref="IWorkflowStep"/>, and maps success or
/// failure to a process exit code.
///
/// <para>The step's <see cref="StepResult.Value"/> is discarded here, and that is not an oversight:
/// <c>ecs:runTask.sync</c> reports the ECS task description back to Step Functions, not anything the
/// container wrote, so there is no channel to return it on. A step whose output something downstream
/// reads belongs on the Lambda target.</para>
///
/// <para><see cref="StepResult.IsInProgress"/> is likewise not mapped to a distinct exit code. It
/// used to be (exit 2, "retry later"), but that code was unobservable: under <c>runTask.sync</c> any
/// non-zero exit from the essential container surfaces as <c>States.TaskFailed</c>, so "still
/// polling" was indistinguishable from a genuine failure and would have been caught and sent to the
/// DLQ while the job it was polling was still running. A polling step belongs on Lambda, where the
/// flag round-trips on the result object.</para>
/// </summary>
public static class EcsHost
{
    public static Task<int> RunAsync(
        IReadOnlyDictionary<string, Func<IWorkflowStep>> steps, ILogger logger, CancellationToken cancellationToken = default) =>
        RunAsync(
            steps, logger,
            Environment.GetEnvironmentVariable(WorkflowEnvironment.Step),
            Environment.GetEnvironmentVariable(WorkflowEnvironment.Input),
            cancellationToken);

    public static async Task<int> RunAsync(
        IReadOnlyDictionary<string, Func<IWorkflowStep>> steps, ILogger logger,
        string? stepName, string? inputJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stepName))
        {
            logger.LogError("{EnvVar} is required but was not set.", WorkflowEnvironment.Step);
            return 1;
        }

        if (!steps.TryGetValue(stepName, out var stepFactory))
        {
            logger.LogError(
                "Unknown {EnvVar} '{Step}'. Known steps: {KnownSteps}",
                WorkflowEnvironment.Step, stepName, string.Join(", ", steps.Keys));
            return 1;
        }

        if (string.IsNullOrWhiteSpace(inputJson))
        {
            logger.LogError("{EnvVar} is required but was not set.", WorkflowEnvironment.Input);
            return 1;
        }

        try
        {
            var step = stepFactory();
            await step.ExecuteAsync(inputJson, cancellationToken);
            logger.LogInformation("Step '{Step}' completed successfully.", stepName);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Step '{Step}' failed.", stepName);
            return 1;
        }
    }
}
