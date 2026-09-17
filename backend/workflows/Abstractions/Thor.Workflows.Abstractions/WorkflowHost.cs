using Microsoft.Extensions.Logging;

namespace Thor.Workflows.Abstractions;

/// <summary>
/// Container entry-point dispatcher shared by every workflow module: reads which step to run
/// and its input from environment variables, resolves the matching <see cref="IWorkflowStep"/>,
/// and maps success/failure to a process exit code so the orchestrator (Step Functions via
/// ECS/Batch task exit status) can apply its own retry/catch/DLQ policy per step.
/// </summary>
public static class WorkflowHost
{
    public const string StepEnvVar = "THOR_STEP";
    public const string InputEnvVar = "THOR_INPUT";

    public static Task<int> RunAsync(
        IReadOnlyDictionary<string, Func<IWorkflowStep>> steps, ILogger logger, CancellationToken cancellationToken = default) =>
        RunAsync(
            steps, logger,
            Environment.GetEnvironmentVariable(StepEnvVar),
            Environment.GetEnvironmentVariable(InputEnvVar),
            cancellationToken);

    public static async Task<int> RunAsync(
        IReadOnlyDictionary<string, Func<IWorkflowStep>> steps, ILogger logger,
        string? stepName, string? inputJson, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stepName))
        {
            logger.LogError("{EnvVar} is required but was not set.", StepEnvVar);
            return 1;
        }

        if (!steps.TryGetValue(stepName, out var stepFactory))
        {
            logger.LogError(
                "Unknown {EnvVar} '{Step}'. Known steps: {KnownSteps}",
                StepEnvVar, stepName, string.Join(", ", steps.Keys));
            return 1;
        }

        if (string.IsNullOrWhiteSpace(inputJson))
        {
            logger.LogError("{EnvVar} is required but was not set.", InputEnvVar);
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
