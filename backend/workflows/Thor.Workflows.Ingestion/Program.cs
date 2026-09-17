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

return await WorkflowHost.RunAsync(steps, logger);
