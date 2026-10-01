namespace Thor.Workflows.Ownership;

/// <summary>
/// The <c>THOR_STEP</c> values this module answers to. Named here so <c>Program.cs</c>'s handler
/// dictionary and the <c>steps</c> map in <c>infra/src/workflow_definitions.tf</c> cannot disagree
/// silently — the same reason ATRE has an <c>AtreSteps</c>.
/// </summary>
public static class OwnershipSteps
{
    /// <summary>Opens the run's workflow row, seeds the rule catalog and hands back the first wave. Runs once.</summary>
    public const string StartRun = "start-run";

    /// <summary>Propagates owners down the hierarchies the phase's walk rules follow, before that phase votes. Loops until done.</summary>
    public const string Walk = "walk";

    /// <summary>The Distributed Map body: matches one chunk of one phase's entities in scope.</summary>
    public const string Vote = "vote";

    /// <summary>Reads what the wave just did and builds the next one — possibly the next phase's first — or says there is none.</summary>
    public const string NextWave = "next-wave";

    /// <summary>Writes this run's rank-1 winners to S3 as OWNED_BY edges and starts a Neptune bulk load.</summary>
    public const string GraphLoadStart = "graph-load-start";

    /// <summary>Checks the bulk load once per invocation; the state machine loops it while it is still running.</summary>
    public const string GraphLoadPoll = "graph-load-poll";

    /// <summary>Closes the run's workflow row and drops its walk staging, after the graph load. Runs once.</summary>
    public const string Finalize = "finalize";

    /// <summary>Closes the run's workflow row as failed. On the Catch path, when the run itself dies.</summary>
    public const string RecordFailure = "record-failure";

    /// <summary>Records one tolerated chunk's lost window against the run, which then keeps going.</summary>
    public const string RecordChunkFailure = "record-chunk-failure";
}
