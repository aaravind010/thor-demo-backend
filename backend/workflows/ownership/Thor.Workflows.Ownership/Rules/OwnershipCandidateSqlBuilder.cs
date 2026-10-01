using System.Text;
using Npgsql;
using Thor.Workflows.Ownership.Constants;

namespace Thor.Workflows.Ownership.Rules;

/// <summary>
/// Assembles one entity-type phase's candidate query for one window of that phase: a <c>scoped</c>
/// CTE (not-deleted + manifest scoping + an <see cref="OwnershipAssignmentScope"/>-driven
/// ownership-state filter, then the window's <c>OFFSET</c>/<c>LIMIT</c>), one compiled CTE per active
/// rule, a <c>UNION ALL</c> of them tagged with each rule's own id/weight, and a "highest weight wins"
/// collapse per (entity, identity) pair. Every rule here comes from <see cref="OwnershipRuleLoader"/>,
/// so this class has zero knowledge of what any specific rule does.
///
/// <para>Binds <c>@runId</c> for every query, which compiled rules may rely on — see
/// <see cref="OwnershipRuleCompiler"/>'s walk rules.</para>
/// </summary>
internal static class OwnershipCandidateSqlBuilder
{
    public static (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) Build(
        string entityType, IReadOnlyList<ParsedOwnershipRule> rules, Guid? scanManifestId, string assignmentScope,
        string runId, int offset, int limit)
    {
        var (scopedSql, parameters) = BuildScoped(entityType, scanManifestId, assignmentScope, runId, offset, limit);

        var sql = new StringBuilder("WITH ").Append(scopedSql);

        var combinedArms = new List<string>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var fragment = OwnershipRuleCompiler.Compile(rule, i);
            parameters.AddRange(fragment.Parameters);

            foreach (var (name, body) in fragment.AuxiliaryCtes)
            {
                sql.Append(",\n").Append(name).Append(" AS (\n").Append(body).Append("\n)");
            }

            var ruleCteName = $"rule_{i}";
            sql.Append(",\n").Append(ruleCteName).Append(" AS (\n").Append(fragment.MainCteBody).Append("\n)");

            var ruleIdParam = $"ruleid{i}";
            var weightParam = $"weight{i}";
            parameters.Add(new NpgsqlParameter(ruleIdParam, rule.Id));
            parameters.Add(new NpgsqlParameter(weightParam, rule.Weight));
            combinedArms.Add($"SELECT entity_id, identity_id, @{ruleIdParam}::uuid AS rule_id, @{weightParam}::numeric AS weight FROM {ruleCteName}");
        }

        if (combinedArms.Count == 0)
        {
            // No active rules for this entity type this run — short-circuit rather than emit an invalid empty UNION ALL.
            return ("SELECT NULL::uuid AS entity_id, NULL::uuid AS identity_id, NULL::uuid AS rule_id, NULL::numeric AS weight WHERE FALSE", parameters);
        }

        sql.Append(",\ncombined AS (\n").Append(string.Join("\nUNION ALL\n", combinedArms)).Append("\n)");
        sql.Append("""
            ,
            earliest AS (
                SELECT DISTINCT ON (entity_id, identity_id) entity_id, identity_id, rule_id, weight
                FROM combined
                ORDER BY entity_id, identity_id, weight DESC, rule_id
            )
            SELECT entity_id, identity_id, rule_id, weight
            FROM earliest
            ORDER BY entity_id, weight DESC, identity_id
            """);

        return (sql.ToString(), parameters);
    }

    /// <summary>
    /// How many in-scope entities the window holds, matched or not — the count the wave planner needs
    /// to tell a full window from the phase's last. The same <c>scoped</c> CTE the candidate query
    /// uses, so the two can never disagree about what the window is.
    /// </summary>
    public static (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildWindowCount(
        string entityType, Guid? scanManifestId, string assignmentScope, string runId, int offset, int limit)
    {
        var (scopedSql, parameters) = BuildScoped(entityType, scanManifestId, assignmentScope, runId, offset, limit);
        return ($"WITH {scopedSql}\nSELECT count(*)::int FROM scoped", parameters);
    }

    /// <summary>
    /// The window, ordered by id so an offset names the same rows on every invocation. That holds only
    /// because <see cref="BuildAssignmentFilter"/> ignores this run's own rows: the run writes
    /// <c>party_assignment</c> as it goes, and a filter that saw those writes would shrink the scope
    /// under the later windows and shift every offset past the entities they were meant to cover.
    /// </summary>
    private static (string Sql, List<NpgsqlParameter> Parameters) BuildScoped(
        string entityType, Guid? scanManifestId, string assignmentScope, string runId, int offset, int limit)
    {
        var scope = EntityScopeCatalog.Resolve(entityType);
        var parameters = new List<NpgsqlParameter>
        {
            new("entityTypeParam", entityType),
            new("runId", runId),
            new("windowOffset", offset),
            new("windowLimit", limit),
        };

        var manifestScope = "";
        if (scanManifestId is { } manifestId)
        {
            manifestScope = "AND e.id IN (SELECT entity_id FROM tenant.ingest_change_event WHERE scan_manifest_id = @manifestId AND entity_type = @entityTypeParam)";
            parameters.Add(new NpgsqlParameter("manifestId", manifestId));
        }

        var notDeleted = scope.NotDeletedPredicate?.Replace("{alias}", "e");
        var assignmentFilter = BuildAssignmentFilter(assignmentScope);

        var sql = $$"""
            scoped AS (
                SELECT e.id
                FROM {{scope.Table}} e
                WHERE 1 = 1
                  {{(notDeleted is null ? "" : $"AND {notDeleted}")}}
                  {{manifestScope}}
                  {{assignmentFilter}}
                ORDER BY e.id
                OFFSET @windowOffset
                LIMIT @windowLimit
            )
            """;

        return (sql, parameters);
    }

    /// <summary>
    /// The <c>scoped</c> CTE's ownership-state predicate, keyed by <see cref="OwnershipAssignmentScope"/>.
    /// "Proposed" is an active, non-override <c>party_assignment</c> row; "confirmed" is an active,
    /// override row — see <see cref="OwnershipAssignmentScope"/> for why those map that way. Rows this
    /// run wrote never count: see <see cref="BuildScoped"/>.
    /// </summary>
    private static string BuildAssignmentFilter(string assignmentScope) => assignmentScope switch
    {
        OwnershipAssignmentScope.All => "",
        OwnershipAssignmentScope.UnassignedOnly => """
            AND NOT EXISTS (
                SELECT 1 FROM tenant.party_assignment pa
                WHERE pa.entity_type = @entityTypeParam AND pa.is_active = TRUE AND pa.entity_id = e.id
                  AND pa.run_id <> @runId
            )
            """,
        OwnershipAssignmentScope.UnassignedAndProposed => """
            AND NOT EXISTS (
                SELECT 1 FROM tenant.party_assignment pa
                WHERE pa.entity_type = @entityTypeParam AND pa.is_active = TRUE
                  AND pa.is_override = TRUE AND pa.entity_id = e.id
                  AND pa.run_id <> @runId
            )
            """,
        OwnershipAssignmentScope.UnassignedAndConfirmed => """
            AND NOT EXISTS (
                SELECT 1 FROM tenant.party_assignment pa
                WHERE pa.entity_type = @entityTypeParam AND pa.is_active = TRUE
                  AND pa.is_override = FALSE AND pa.entity_id = e.id
                  AND pa.run_id <> @runId
            )
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(assignmentScope), assignmentScope, "Unknown Ownership assignment scope."),
    };
}
