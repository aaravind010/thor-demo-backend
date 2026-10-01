using Microsoft.Extensions.Logging;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting;
using Thor.Workflows.Ownership;
using Thor.Workflows.Ownership.Steps;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("Thor.Workflows.Ownership");

var steps = new Dictionary<string, Func<IWorkflowStep>>
{
    // Opens the run's workflow row, seeds the rule catalog and hands back the first wave. Runs once.
    [OwnershipSteps.StartRun] = () => new OwnershipStartRunStep(),

    // Stages each phase's walk-rule candidates before that phase votes. Loops until done.
    [OwnershipSteps.Walk] = () => new OwnershipWalkStep(),

    // The Map body, one invocation per chunk. Holds no run-level state of its own.
    [OwnershipSteps.Vote] = () => new OwnershipVoteStep(),

    // Reads what the wave just did and builds the next one, or ends the loop.
    [OwnershipSteps.NextWave] = () => new OwnershipNextWaveStep(),

    // Projects this run's winners into Neptune as OWNED_BY edges.
    [OwnershipSteps.GraphLoadStart] = () => new OwnershipGraphLoadStartStep(),
    [OwnershipSteps.GraphLoadPoll] = () => new OwnershipGraphLoadPollStep(),

    // Closes the run's workflow row, after the graph load.
    [OwnershipSteps.Finalize] = () => new OwnershipFinalizeStep(),

    // Closes the run's workflow row as failed, from the definition's Catch path.
    [OwnershipSteps.RecordFailure] = () => new OwnershipRecordFailureStep(),

    // Writes down one chunk's lost window so the run can carry on without losing track of it.
    [OwnershipSteps.RecordChunkFailure] = () => new OwnershipRecordChunkFailureStep(),
};

return await WorkflowEntryPoint.RunAsync(steps, logger);
