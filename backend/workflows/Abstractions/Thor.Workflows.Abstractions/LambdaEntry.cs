using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Microsoft.Extensions.Logging;

namespace Thor.Workflows.Abstractions;

/// <summary>
/// Lambda entry point shared by every workflow module: resolves the <see cref="IWorkflowStep"/>
/// named by <see cref="WorkflowHost.StepEnvVar"/> (one Lambda function per step, distinguished
/// only by that env var) and boots a Lambda custom runtime that invokes it per request. The raw
/// Lambda event body is passed straight through as the step's input, and the step's own result
/// object is serialized straight back out as the Lambda's response — so a Step Functions
/// <c>Choice</c> state can read whatever fields the result carries directly off the Lambda's
/// JSON output. A thrown exception propagates unhandled, surfacing as a Lambda invocation error
/// for a <c>Catch</c> block to route to DLQ.
/// </summary>
public static class LambdaEntry
{
    private static readonly TimeSpan CancellationMargin = TimeSpan.FromSeconds(2);

    public static async Task RunAsync(IReadOnlyDictionary<string, Func<IWorkflowStep>> steps, ILogger logger)
    {
        var stepName = Environment.GetEnvironmentVariable(WorkflowHost.StepEnvVar)
            ?? throw new InvalidOperationException($"{WorkflowHost.StepEnvVar} is required but was not set.");

        if (!steps.TryGetValue(stepName, out var stepFactory))
        {
            throw new InvalidOperationException($"Unknown {WorkflowHost.StepEnvVar} '{stepName}'. Known steps: {string.Join(", ", steps.Keys)}");
        }

        var handler = BuildHandler(stepFactory(), stepName, logger);

        using var bootstrap = LambdaBootstrapBuilder.Create(handler).Build();
        await bootstrap.RunAsync();
    }

    /// <summary>Split out from <see cref="RunAsync"/> so tests can drive the request/response marshalling directly, without a real Lambda Runtime API loop.</summary>
    internal static Func<Stream, ILambdaContext, Task<Stream>> BuildHandler(IWorkflowStep step, string stepName, ILogger logger) =>
        async (inputStream, context) =>
        {
            using var reader = new StreamReader(inputStream, Encoding.UTF8);
            var inputJson = await reader.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeoutUntilMargin(context.RemainingTime));
            logger.LogInformation("Invoking step '{Step}' (request {AwsRequestId}).", stepName, context.AwsRequestId);
            var (result, _) = await step.ExecuteAsync(inputJson, cts.Token);

            var responseJson = JsonSerializer.Serialize(result);
            return new MemoryStream(Encoding.UTF8.GetBytes(responseJson));
        };

    private static TimeSpan TimeoutUntilMargin(TimeSpan remaining) =>
        remaining > CancellationMargin ? remaining - CancellationMargin : TimeSpan.Zero;
}
