namespace Thor.Workflows.Ingestion;

/// <summary>
/// The <c>THOR_STEP</c> values this module answers to. Named here so <c>Program.cs</c>'s handler
/// dictionary and <see cref="IngestionGraph"/> cannot disagree — the orchestration used to live in
/// HCL and repeat these strings, with nothing checking that the two spellings matched.
/// </summary>
public static class IngestionSteps
{
    /// <summary>Sizes the manifest and writes $.Compute; the four real steps then run on whichever target it picked.</summary>
    public const string SelectCompute = "select-compute";

    /// <summary>Runs twice against one task definition: once in list mode, once as the Distributed Map body.</summary>
    public const string ExtractStage = "extract-stage";

    public const string Promote = "promote";

    public const string GraphLoadStart = "graph-load-start";

    public const string GraphLoadPoll = "graph-load-poll";

    /// <summary>Role variant for the two steps that touch the Neptune bulk-load bucket.</summary>
    public const string GraphLoadRole = "graph-load";
}
