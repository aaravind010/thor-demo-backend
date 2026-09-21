using Microsoft.Extensions.Logging;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Ingestion.Steps;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("Thor.Workflows.Ingestion");

var steps = new Dictionary<string, Func<IWorkflowStep>>
{
    ["extract-stage"] = () => new ExtractAndStageStep(),
    ["promote"] = () => new PromoteStep(),
    ["graph-load-start"] = () => new GraphLoadStartStep(),
    ["graph-load-poll"] = () => new GraphLoadPollStep(),
};

// AWS_LAMBDA_RUNTIME_API is set by the Lambda execution environment for every invocation of a
// container image running as a Lambda function; it is never set under ECS/Batch. One container
// image, one process entrypoint, two possible compute targets.
if (Environment.GetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API") is not null)
{
    await LambdaEntry.RunAsync(steps, logger);
    return 0;
}

return await WorkflowHost.RunAsync(steps, logger);
