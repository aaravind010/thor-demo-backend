namespace Thor.Workflows.Ownership;

/// <summary>
/// How much work an Ownership run does at a time. The same two dials as ATRE's, for the same reason:
/// the right values depend on an environment's Aurora capacity, not on anything about Ownership — see
/// <c>infra/src/workflow_definitions.tf</c>. Plus the walk step's time budget, which is about the
/// Lambda timeout.
/// </summary>
public static class OwnershipWaves
{
    /// <summary>In-scope entities one Map item reads — the dial that keeps a vote invocation inside the Lambda timeout.</summary>
    public const string EntitiesPerChunkEnvVar = "THOR_OWNERSHIP_ENTITIES_PER_CHUNK";

    /// <summary>Chunks handed to one Map. Pair it with the Map's MaxConcurrency: fewer leaves capacity idle.</summary>
    public const string ChunksPerWaveEnvVar = "THOR_OWNERSHIP_CHUNKS_PER_WAVE";

    /// <summary>How long one walk invocation keeps starting new levels before handing back to the state machine.</summary>
    public const string WalkBudgetSecondsEnvVar = "THOR_OWNERSHIP_WALK_BUDGET_SECONDS";

    /// <summary>
    /// ATRE's figure, unmeasured here too. A chunk's cost is its share of every active rule's SQL
    /// rather than per-entity work in memory, so the real number may differ in either direction;
    /// it is configuration so correcting it is not a redeploy.
    /// </summary>
    public const int DefaultEntitiesPerChunk = 10_000;

    /// <summary>Matches the Map's default MaxConcurrency, so a wave is exactly one full round of concurrent invocations.</summary>
    public const int DefaultChunksPerWave = 5;

    /// <summary>
    /// Ten minutes of a fifteen-minute Lambda timeout. A level is one statement and is never cut
    /// short, so the budget only decides whether to start another; the remaining five minutes are
    /// headroom for the level already running when the budget expires.
    /// </summary>
    public const int DefaultWalkBudgetSeconds = 600;

    public static int EntitiesPerChunkFromEnvironment() =>
        PositiveEnvVar(EntitiesPerChunkEnvVar, DefaultEntitiesPerChunk);

    public static int ChunksPerWaveFromEnvironment() =>
        PositiveEnvVar(ChunksPerWaveEnvVar, DefaultChunksPerWave);

    public static TimeSpan WalkBudgetFromEnvironment() =>
        TimeSpan.FromSeconds(PositiveEnvVar(WalkBudgetSecondsEnvVar, DefaultWalkBudgetSeconds));

    /// <summary>Unset, unparseable or non-positive all fall back to the default — a zero here would make a wave do nothing, forever.</summary>
    private static int PositiveEnvVar(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
}
