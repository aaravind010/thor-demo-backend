using Microsoft.Extensions.Logging;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Atre;
using Thor.Workflows.Atre.Steps;
using Thor.Workflows.Hosting;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("Thor.Workflows.Atre");

var steps = new Dictionary<string, Func<IWorkflowStep>>
{
    // Opens the run's workflow row and hands back the first wave. Runs once.
    [AtreSteps.StartRun] = () => new AtreStartRunStep(),

    // The Map body, one invocation per chunk. Holds no run-level state of its own.
    [AtreSteps.Classify] = () => new AtreClassifyStep(),

    // Reads what the wave just did and builds the next one, or ends the loop.
    [AtreSteps.NextWave] = () => new AtreNextWaveStep(),

    // Closes the run's workflow row, after the last wave.
    [AtreSteps.Finalize] = () => new AtreFinalizeStep(),

    // Closes the run's workflow row as failed. Invoked from the definition's Catch path, which is the
    // only place that sees a timed-out or OOM-killed chunk — no catch block runs inside one.
    [AtreSteps.RecordFailure] = () => new AtreRecordFailureStep(),

    // Writes down one chunk's lost window so the run can carry on without losing track of it.
    [AtreSteps.RecordChunkFailure] = () => new AtreRecordChunkFailureStep(),
};

// One container image, one process entry point, two possible compute targets. Which host to start is
// WorkflowEntryPoint's call, not this module's — every workflow made the same decision the same way.
return await WorkflowEntryPoint.RunAsync(steps, logger);
