using Microsoft.Extensions.Logging;
using Thor.Workflows.Abstractions;

namespace Thor.Workflows.Hosting;

/// <summary>
/// The single entry point a workflow module's <c>Program.cs</c> calls. One container image, one
/// process entry point, two possible compute targets — this decides which host to start.
///
/// <para>Each module used to make this choice itself, so the heuristic was copy-pasted per workflow.
/// It lives here instead, because which compute target a module can run on is a platform concern:
/// the same image is registered as both ECS task definitions and image-packaged Lambda functions,
/// and only the runtime environment knows which one is executing it.</para>
/// </summary>
public static class WorkflowEntryPoint
{
    public static async Task<int> RunAsync(
        IReadOnlyDictionary<string, Func<IWorkflowStep>> steps, ILogger logger, CancellationToken cancellationToken = default)
    {
        // Set by the Lambda execution environment for every invocation of a container image running
        // as a Lambda function; never set under ECS.
        if (Environment.GetEnvironmentVariable(WorkflowEnvironment.LambdaRuntimeApi) is not null)
        {
            await LambdaHost.RunAsync(steps, logger);
            return 0;
        }

        return await EcsHost.RunAsync(steps, logger, cancellationToken);
    }
}
