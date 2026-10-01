using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Thor.DataLayer.Models.Tenants;
using Thor.Rules.Ownership;
using Thor.Workflows.Ownership.Rules;
using Thor.Workflows.Ownership.Tests.Fixtures;
using Thor.Workflows.Ownership.Walk;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Walk;

/// <summary>
/// Frontier propagation against the recursive CTE it replaced. The oracle is that CTE, kept here
/// verbatim in shape (walk up from every entity to <c>maxDepth</c>, keep the nearest owned
/// ancestor): whatever else changed, each entity must inherit from an owner at the same minimum
/// depth the recursive walk found. Ties at that depth are where the two may differ — the recursive
/// walk picked arbitrarily, propagation picks the lowest identity id — so ties are checked for
/// membership in the tied set rather than equality.
/// </summary>
public sealed class OwnershipWalkPropagatorTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private const int MaxDepth = 10;

    [Fact]
    public async Task Chain_EachLevelInheritsTheNearestOwner()
    {
        var owner = await db.AddIdentityAsync("W1", "Top Owner", "top@corp.com");
        var (rule, groups) = await ChainAsync("chain", length: 4);
        await db.AddPriorOwnerAsync("grp", groups[0].Id, owner.Id);

        var runId = await PropagateAsync(rule);

        var staged = await StagedAsync(runId, rule.Id);
        Assert.Equal([1, 2, 3], groups.Skip(1).Select(g => staged[g.Id].Depth));
        Assert.All(groups.Skip(1), g => Assert.Equal(owner.Id, staged[g.Id].IdentityId));
        Assert.False(staged.ContainsKey(groups[0].Id)); // an owner never inherits from itself
    }

    [Fact]
    public async Task OwnedIntermediate_IsNearerThanItsOwnAncestor()
    {
        var top = await db.AddIdentityAsync("W2", "Top", "top2@corp.com");
        var middle = await db.AddIdentityAsync("W3", "Middle", "mid@corp.com");
        var (rule, groups) = await ChainAsync("nearer", length: 3);
        await db.AddPriorOwnerAsync("grp", groups[0].Id, top.Id);
        await db.AddPriorOwnerAsync("grp", groups[1].Id, middle.Id);

        var runId = await PropagateAsync(rule);

        var staged = await StagedAsync(runId, rule.Id);
        Assert.Equal(middle.Id, staged[groups[2].Id].IdentityId);
        Assert.Equal(1, staged[groups[2].Id].Depth);
        Assert.Equal(top.Id, staged[groups[1].Id].IdentityId); // its own owner does not count for itself
    }

    [Fact]
    public async Task Cycle_Terminates_AndMatchesTheRecursiveWalk()
    {
        var owner = await db.AddIdentityAsync("W4", "Cycle Owner", "cycle@corp.com");
        var rule = await WalkRuleAsync("cycle");
        var a = await db.AddGroupAsync("cycle-a");
        var b = await db.AddGroupAsync("cycle-b");
        await db.AddEdgeAsync(a.Id, "grp", b.Id, "grp", "MEMBER_OF");
        await db.AddEdgeAsync(b.Id, "grp", a.Id, "grp", "MEMBER_OF");
        await db.AddPriorOwnerAsync("grp", a.Id, owner.Id);

        var runId = await PropagateAsync(rule);

        var staged = await StagedAsync(runId, rule.Id);
        Assert.Equal(1, staged[b.Id].Depth);
        // The recursive walk found a's own owner two hops round the cycle; propagation does too.
        Assert.Equal(2, staged[a.Id].Depth);
        await AssertMatchesOracleAsync(rule, runId, [a.Id, b.Id]);
    }

    [Fact]
    public async Task DepthCap_StopsAtMaxDepth()
    {
        var owner = await db.AddIdentityAsync("W5", "Deep Owner", "deep@corp.com");
        var (rule, groups) = await ChainAsync("capped", length: 6, maxDepth: 3);
        await db.AddPriorOwnerAsync("grp", groups[0].Id, owner.Id);

        var runId = await PropagateAsync(rule);

        var staged = await StagedAsync(runId, rule.Id);
        Assert.Equal(3, groups.Count(g => staged.ContainsKey(g.Id)));
        Assert.False(staged.ContainsKey(groups[4].Id));
    }

    [Fact]
    public async Task ThisRunsOwnRows_DoNotSeedTheWalk()
    {
        var owner = await db.AddIdentityAsync("W6", "Same Run", "samerun@corp.com");
        var (rule, groups) = await ChainAsync("same-run", length: 2);
        var runId = Guid.NewGuid().ToString();
        db.Context.PartyAssignments.Add(new PartyAssignment
        {
            Id = Guid.NewGuid(), EntityType = "grp", EntityId = groups[0].Id, IdentityId = owner.Id, Rank = 1,
            IsActive = true, VoteDistribution = "{}", ContributingRuleIds = [], PrecisionScoreSnapshot = "{}",
            RunId = runId, AssignedAt = DateTimeOffset.UtcNow,
        });
        await db.Context.SaveChangesAsync();

        await PropagateAsync(rule, runId);

        Assert.False((await StagedAsync(runId, rule.Id)).ContainsKey(groups[1].Id));
    }

    [Fact]
    public async Task AssetParentColumn_PropagatesDownTheTree()
    {
        var owner = await db.AddIdentityAsync("W7", "Share Owner", "share@corp.com");
        var rule = await ParseAsync(OwnershipRuleDefaults.AssetParentInheritance);
        var root = await db.AddAssetAsync("walk-root");
        var mid = await db.AddAssetAsync("walk-mid", root.Id);
        var leaf = await db.AddAssetAsync("walk-leaf", mid.Id);
        await db.AddPriorOwnerAsync("asset", root.Id, owner.Id);

        var runId = await PropagateAsync(rule);

        var staged = await StagedAsync(runId, rule.Id);
        Assert.Equal((owner.Id, 1), staged[mid.Id]);
        Assert.Equal((owner.Id, 2), staged[leaf.Id]);
    }

    [Fact]
    public async Task RandomDagWithDiamondsAndCycles_MatchesTheRecursiveWalk()
    {
        var rule = await WalkRuleAsync("random-dag");
        var random = new Random(81);
        var groups = new List<Grp>();
        for (var i = 0; i < 120; i++)
        {
            groups.Add(await db.AddGroupAsync($"dag-{i}"));
        }

        // Mostly child → earlier parent (a DAG with plenty of diamonds), plus a few back-edges for cycles.
        var edges = new HashSet<(int, int)>();
        for (var child = 1; child < groups.Count; child++)
        {
            var parents = random.Next(1, 4);
            for (var p = 0; p < parents; p++)
            {
                edges.Add((child, random.Next(0, child)));
            }
        }

        for (var i = 0; i < 6; i++)
        {
            var from = random.Next(0, groups.Count / 2);
            edges.Add((from, random.Next(from + 1, groups.Count)));
        }

        foreach (var (child, parent) in edges)
        {
            await db.AddEdgeAsync(groups[child].Id, "grp", groups[parent].Id, "grp", "MEMBER_OF");
        }

        var identities = new List<IdentityRecord>();
        for (var i = 0; i < 4; i++)
        {
            identities.Add(await db.AddIdentityAsync($"DAG{i}", $"Dag Owner {i}", $"dag{i}@corp.com"));
        }

        foreach (var index in Enumerable.Range(0, groups.Count).Where(i => random.Next(0, 9) == 0))
        {
            await db.AddPriorOwnerAsync("grp", groups[index].Id, identities[random.Next(0, identities.Count)].Id);
        }

        var runId = await PropagateAsync(rule);

        await AssertMatchesOracleAsync(rule, runId, groups.Select(g => g.Id).ToList());
    }

    [Fact]
    public async Task ExhaustedBudget_ResumesWhereItStopped_ToTheSameResult()
    {
        var owner = await db.AddIdentityAsync("W8", "Resume Owner", "resume@corp.com");
        var (rule, groups) = await ChainAsync("resume", length: 5);
        await db.AddPriorOwnerAsync("grp", groups[0].Id, owner.Id);
        var runId = Guid.NewGuid().ToString();

        // A zero budget runs exactly one level per call.
        var calls = 0;
        WalkProgress progress;
        do
        {
            progress = await new OwnershipWalkPropagator(db.NewContext(), runId, NullLogger.Instance)
                .RunAsync([rule], TimeSpan.Zero, CancellationToken.None);
            calls++;
        }
        while (!progress.IsComplete && calls < 20);

        Assert.True(progress.IsComplete);
        Assert.True(calls > 1);
        var staged = await StagedAsync(runId, rule.Id);
        Assert.Equal([1, 2, 3, 4], groups.Skip(1).Select(g => staged[g.Id].Depth));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<ParsedOwnershipRule> WalkRuleAsync(string name, int maxDepth = MaxDepth)
    {
        var now = DateTimeOffset.UtcNow;
        var row = new OwnershipRule
        {
            Id = Guid.NewGuid(),
            RuleName = $"test-walk-{name}",
            RuleType = OwnershipRuleType.InheritOwner,
            RuleDefinition = $$"""{"schemaVersion":1,"walk":{"relType":"MEMBER_OF","direction":"out","toType":"grp"},"maxDepth":{{maxDepth}}}""",
            AppliesTo = "grp",
            IsActive = false, // never picked up by another test's loader
            PrecisionScore = 0.75m,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Context.OwnershipRules.Add(row);
        await db.Context.SaveChangesAsync();
        return Parse(row);
    }

    private async Task<ParsedOwnershipRule> ParseAsync(string ruleName)
    {
        await db.SeedRulesAsync();
        return Parse(await db.Context.OwnershipRules.AsNoTracking().SingleAsync(r => r.RuleName == ruleName));
    }

    private static ParsedOwnershipRule Parse(OwnershipRule row)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<RuleDefinitionPayload>(row.RuleDefinition)!;
        Assert.True(RuleDefinitionValidator.TryValidate(row.RuleType, row.AppliesTo, payload, out var error), error);
        return new ParsedOwnershipRule(row.Id, row.RuleName, row.RuleType, row.AppliesTo, row.PrecisionScore, payload);
    }

    /// <summary>groups[i] is a MEMBER_OF child of groups[i-1], so groups[0] is the top.</summary>
    private async Task<(ParsedOwnershipRule Rule, List<Grp> Groups)> ChainAsync(string name, int length, int maxDepth = MaxDepth)
    {
        var rule = await WalkRuleAsync(name, maxDepth);
        var groups = new List<Grp>();
        for (var i = 0; i < length; i++)
        {
            groups.Add(await db.AddGroupAsync($"{name}-{i}"));
            if (i > 0)
            {
                await db.AddEdgeAsync(groups[i].Id, "grp", groups[i - 1].Id, "grp", "MEMBER_OF");
            }
        }

        return (rule, groups);
    }

    private async Task<string> PropagateAsync(ParsedOwnershipRule rule, string? runId = null)
    {
        runId ??= Guid.NewGuid().ToString();
        var progress = await new OwnershipWalkPropagator(db.NewContext(), runId, NullLogger.Instance)
            .RunAsync([rule], TimeSpan.MaxValue, CancellationToken.None);
        Assert.True(progress.IsComplete);
        return runId;
    }

    private async Task<Dictionary<Guid, (Guid IdentityId, int Depth)>> StagedAsync(string runId, Guid ruleId) =>
        await db.Context.OwnershipWalkCandidates.AsNoTracking()
            .Where(c => c.RunId == runId && c.RuleId == ruleId)
            .ToDictionaryAsync(c => c.EntityId, c => (c.IdentityId, c.Depth));

    /// <summary>
    /// The recursive walk the propagator replaced, from <paramref name="starts"/>: every (start,
    /// depth, owner) it reaches. The nearest depth per start is what the old rule picked from.
    /// </summary>
    private async Task AssertMatchesOracleAsync(ParsedOwnershipRule rule, string runId, IReadOnlyList<Guid> starts)
    {
        const string oracleSql = """
            WITH RECURSIVE anc AS (
                SELECT s AS start_id, s AS node_id, 0 AS depth FROM unnest(@starts) s
                UNION ALL
                SELECT anc.start_id, he.to_id, anc.depth + 1
                FROM anc
                JOIN tenant.edge he ON he.rel_type = 'MEMBER_OF' AND he.is_deleted = FALSE
                 AND he.from_id = anc.node_id AND he.from_type = 'grp' AND he.to_type = 'grp'
                WHERE anc.depth < @maxDepth
            )
            SELECT anc.start_id, anc.depth, pa.identity_id
            FROM anc
            JOIN tenant.party_assignment pa
              ON pa.entity_type = 'grp' AND pa.entity_id = anc.node_id AND pa.is_active = TRUE AND pa.rank = 1
             AND pa.run_id <> @runId
            WHERE anc.depth > 0
            """;

        var reached = new List<(Guid Start, int Depth, Guid IdentityId)>();
        await using (var connection = new NpgsqlConnection(db.Context.Database.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(oracleSql, connection);
            command.Parameters.AddWithValue("starts", starts.ToArray());
            command.Parameters.AddWithValue("maxDepth", rule.Definition.MaxDepth ?? MaxDepth);
            command.Parameters.AddWithValue("runId", runId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                reached.Add((reader.GetGuid(0), reader.GetInt32(1), reader.GetGuid(2)));
            }
        }

        var expected = reached
            .GroupBy(r => r.Start)
            .ToDictionary(g => g.Key, g =>
            {
                var nearest = g.Min(r => r.Depth);
                return (Depth: nearest, Owners: g.Where(r => r.Depth == nearest).Select(r => r.IdentityId).ToHashSet());
            });

        var staged = await StagedAsync(runId, rule.Id);
        var startSet = starts.ToHashSet();

        Assert.Equal(expected.Keys.OrderBy(k => k), staged.Keys.Where(startSet.Contains).OrderBy(k => k));
        foreach (var (start, (depth, owners)) in expected)
        {
            Assert.Equal(depth, staged[start].Depth);
            Assert.Contains(staged[start].IdentityId, owners);

            // Postgres orders uuids bytewise, which is the canonical string's order — not Guid's.
            Assert.Equal(owners.MinBy(o => o.ToString()), staged[start].IdentityId);
        }
    }
}
