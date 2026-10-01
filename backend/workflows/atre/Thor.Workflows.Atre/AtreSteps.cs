namespace Thor.Workflows.Atre;

/// <summary>
/// The <c>THOR_STEP</c> values this module answers to. Named here so <c>Program.cs</c>'s handler
/// dictionary and the <c>steps</c> map in <c>infra/src/workflow_definitions.tf</c> cannot disagree
/// silently — the same reason ingestion has an <c>IngestionSteps</c>.
/// </summary>
public static class AtreSteps
{
    /// <summary>Opens the run's workflow row and hands back the first wave. Runs once.</summary>
    public const string StartRun = "start-run";

    /// <summary>The Distributed Map body: classifies one chunk of the accounts in scope.</summary>
    public const string Classify = "classify";

    /// <summary>Reads what the wave just did and builds the next one, or says there is none. Runs once per wave.</summary>
    public const string NextWave = "next-wave";

    /// <summary>Closes the run's workflow row, after the last wave. Runs once.</summary>
    public const string Finalize = "finalize";

    /// <summary>Closes the run's workflow row as failed. On the Catch path, when the run itself dies.</summary>
    public const string RecordFailure = "record-failure";

    /// <summary>Records one tolerated chunk's lost window against the run, which then keeps going.</summary>
    public const string RecordChunkFailure = "record-chunk-failure";
}
