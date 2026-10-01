namespace Thor.Workflows.Atre;

/// <summary>
/// How much work an ATRE run does at a time. Two numbers, both environment configuration rather than
/// constants, because the right values depend on how much Aurora capacity an environment has rather
/// than on anything about ATRE — see <c>infra/src/workflow_definitions.tf</c>.
///
/// <list type="bullet">
/// <item><b>Accounts per chunk</b> is one <see cref="Steps.AtreClassifyStep"/> invocation's worth.
/// Sized by what fits inside the Lambda timeout.</item>
/// <item><b>Chunks per wave</b> is how many the Distributed Map is given at once. The run does a
/// wave, waits for all of it, then comes back for the next — so the state machine never holds more
/// than a wave's worth of anything, whatever the size of the tenant.</item>
/// </list>
/// </summary>
public static class AtreWaves
{
    /// <summary>Rows one Map item reads — the dial that keeps an invocation inside the Lambda timeout.</summary>
    public const string AccountsPerChunkEnvVar = "THOR_ATRE_ACCOUNTS_PER_CHUNK";

    /// <summary>Chunks handed to one Map. Pair it with the Map's MaxConcurrency: fewer leaves capacity idle.</summary>
    public const string ChunksPerWaveEnvVar = "THOR_ATRE_CHUNKS_PER_WAVE";

    /// <summary>
    /// Sized against the Lambda timeout, the only thing it has to respect: <c>lambda_timeout</c>
    /// allows 15 minutes, so 10,000 accounts is about eleven per second, with rule evaluation in
    /// memory and writes flushing every 2,000 rows.
    ///
    /// <para>Honest caveat: nobody has measured the real per-account cost yet. It is an environment
    /// variable so correcting it is a config change. Too high and the symptom is a chunk hitting the
    /// Lambda timeout, retried three times before the Map gives up; it also makes retries coarser,
    /// since a failed chunk redoes all 10,000 — correct whatever the number, as every write ATRE
    /// makes is an <c>ON CONFLICT DO NOTHING</c> upsert, just slower.</para>
    /// </summary>
    public const int DefaultAccountsPerChunk = 10_000;

    /// <summary>Matches the Map's default MaxConcurrency, so a wave is exactly one full round of concurrent invocations.</summary>
    public const int DefaultChunksPerWave = 5;

    public static int AccountsPerChunkFromEnvironment() =>
        PositiveEnvVar(AccountsPerChunkEnvVar, DefaultAccountsPerChunk);

    public static int ChunksPerWaveFromEnvironment() =>
        PositiveEnvVar(ChunksPerWaveEnvVar, DefaultChunksPerWave);

    /// <summary>Unset, unparseable or non-positive all fall back to the default — a zero here would make a wave do nothing, forever.</summary>
    private static int PositiveEnvVar(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;
}
