using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.Rules.Ownership;
using Thor.Workflows.Ownership.Rules;

namespace Thor.Workflows.Ownership.Walk;

/// <summary>What one <see cref="OwnershipWalkPropagator.RunAsync"/> call got through.</summary>
internal sealed record WalkProgress(int LevelsRun, int RowsStaged, bool IsComplete);

/// <summary>
/// Finds, for every entity under an <c>inherit_owner</c> walk rule, its nearest owned ancestor —
/// by breadth-first propagation <em>down</em> from the entities that already have an owner, rather
/// than a recursive walk <em>up</em> from each entity. Stages the answer in
/// <c>ownership_walk_candidate</c>, one row per (run, rule, entity), for the phase's vote chunks to
/// read as that rule's candidates.
///
/// <para>Level 1 is every child of an owned entity, inheriting that owner. Level k is every child
/// of a level k−1 row, inheriting what that row inherited. Each level is one
/// <c>INSERT … SELECT … ON CONFLICT DO NOTHING</c>, and the primary key is the visited-set: an
/// entity reached at an earlier level is never reached again, so each is expanded once per rule
/// however many paths lead to it, and a cycle simply stops. Cost is linear in the edges reached,
/// whatever the shape of the hierarchy.</para>
///
/// <para>Starting from every owned entity at once and stopping at first arrival gives each entity
/// the owner at the smallest depth — the same nearest owned ancestor the recursive walk picked. An
/// entity's own owner never counts for itself (it is a source, not a row), matching the recursive
/// walk's <c>depth &gt; 0</c>. Where two owners are equally near, the lowest identity id wins, and
/// keeping only that one per entity at each level is exact: the nearest owners of a child are the
/// union of its parents' nearest owners, and the minimum of a union is the minimum of the minimums.</para>
///
/// <para>Sources are owners from before this run (<c>run_id &lt;&gt; @runId</c>), so a walk never
/// builds on this run's own same-phase results — deep nesting fills in over successive runs, as it
/// always has — and a retried run seeds from exactly what the first attempt did.</para>
///
/// <para>Resumable: the deepest staged level is the frontier, so an invocation that stops on its
/// time budget is carried on by the next from where it stopped.</para>
/// </summary>
internal sealed class OwnershipWalkPropagator(TenantDbContext db, string runId, ILogger logger)
{
    private const int DefaultMaxDepth = 10;

    /// <summary>Whether <paramref name="rule"/> is one this class propagates — an <c>inherit_owner</c> rule with a <c>walk</c>.</summary>
    public static bool IsWalkRule(ParsedOwnershipRule rule) =>
        rule.RuleType == OwnershipRuleType.InheritOwner && rule.Definition.Walk is not null;

    /// <summary>
    /// Runs levels for every rule in turn until each is exhausted, or until <paramref name="budget"/>
    /// has elapsed. At least one level runs per call, so repeated calls always make progress; the
    /// budget only decides whether to start another, never cuts one short.
    /// </summary>
    public async Task<WalkProgress> RunAsync(
        IReadOnlyList<ParsedOwnershipRule> walkRules, TimeSpan budget, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var levelsRun = 0;
        var rowsStaged = 0;

        foreach (var rule in walkRules)
        {
            var maxDepth = rule.Definition.MaxDepth ?? DefaultMaxDepth;
            var depth = await DeepestStagedLevelAsync(rule.Id, cancellationToken);

            while (depth < maxDepth)
            {
                if (levelsRun > 0 && clock.Elapsed >= budget)
                {
                    return new WalkProgress(levelsRun, rowsStaged, IsComplete: false);
                }

                var inserted = await RunLevelAsync(rule, depth + 1, cancellationToken);
                levelsRun++;
                rowsStaged += inserted;

                logger.LogInformation(
                    "Ownership walk for run {RunId}, rule {RuleName}: level {Level} staged {Rows} row(s).",
                    runId, rule.RuleName, depth + 1, inserted);

                if (inserted == 0)
                {
                    break;
                }

                depth++;
            }
        }

        return new WalkProgress(levelsRun, rowsStaged, IsComplete: true);
    }

    private async Task<int> DeepestStagedLevelAsync(Guid ruleId, CancellationToken cancellationToken) =>
        await db.OwnershipWalkCandidates
            .Where(c => c.RunId == runId && c.RuleId == ruleId)
            .MaxAsync(c => (int?)c.Depth, cancellationToken) ?? 0;

    private async Task<int> RunLevelAsync(ParsedOwnershipRule rule, int level, CancellationToken cancellationToken)
    {
        var (sql, parameters) = BuildLevel(rule, level);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(parameters.ToArray());
            command.CommandTimeout = 0;
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// One level. The frontier is the owned entities themselves at level 1 and the previous level's
    /// rows after that; each child the frontier points at inherits its frontier row's owner. Walk
    /// direction is the direction the recursive walk climbed, so propagation follows it backwards: an
    /// <c>out</c> walk went child → parent along <c>from_id → to_id</c>, so a parent's children are
    /// the <c>from_id</c>s of edges arriving at it.
    /// </summary>
    internal (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildLevel(ParsedOwnershipRule rule, int level)
    {
        var walk = rule.Definition.Walk!;
        var parameters = new List<NpgsqlParameter>
        {
            new("runId", runId),
            new("ruleId", rule.Id),
            new("entityType", rule.AppliesTo),
            new("level", level),
        };

        var frontier = level == 1
            ? """
                SELECT DISTINCT ON (pa.entity_id) pa.entity_id AS node_id, pa.identity_id
                FROM tenant.party_assignment pa
                WHERE pa.entity_type = @entityType AND pa.is_active = TRUE AND pa.rank = 1
                  AND pa.run_id <> @runId
                ORDER BY pa.entity_id, pa.identity_id
                """
            : """
                SELECT w.entity_id AS node_id, w.identity_id
                FROM tenant.ownership_walk_candidate w
                WHERE w.run_id = @runId AND w.rule_id = @ruleId AND w.depth = @level - 1
                """;

        string childJoin;
        string childId;
        if (walk.Column is { } column)
        {
            // Safe to interpolate: RuleDefinitionValidator checked walk.Column against a fixed
            // per-entity-type allowlist before this rule was ever loaded.
            childJoin = $"JOIN {EntityScopeCatalog.Resolve(rule.AppliesTo).Table} child ON child.{column} = src.node_id";
            childId = "child.id";
        }
        else
        {
            parameters.Add(new NpgsqlParameter("relType", walk.RelType));
            var (arrivingSide, leavingSide, childSide) = walk.Direction == "out"
                ? ("to", "from", "from_id")
                : ("from", "to", "to_id");
            childJoin = $"""
                JOIN tenant.edge he
                    ON he.rel_type = @relType AND he.is_deleted = FALSE
                   AND he.{arrivingSide}_id = src.node_id
                   AND he.{arrivingSide}_type = @entityType AND he.{leavingSide}_type = @entityType
                """;
            childId = $"he.{childSide}";
        }

        var sql = $"""
            INSERT INTO tenant.ownership_walk_candidate (run_id, rule_id, entity_id, identity_id, depth)
            SELECT DISTINCT ON (reached.child_id) @runId, @ruleId, reached.child_id, reached.identity_id, @level
            FROM (
                SELECT {childId} AS child_id, src.identity_id
                FROM (
                    {frontier}
                ) src
                {childJoin}
            ) reached
            ORDER BY reached.child_id, reached.identity_id
            ON CONFLICT (run_id, rule_id, entity_id) DO NOTHING
            """;

        return (sql, parameters);
    }
}
