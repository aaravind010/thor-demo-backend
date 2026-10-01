using Npgsql;
using Thor.Rules.Ownership;

namespace Thor.Workflows.Ownership.Rules;

/// <summary>One rule compiled to SQL: zero or more auxiliary CTEs it depends on, plus its own <c>(entity_id, identity_id)</c>-producing CTE body.</summary>
internal sealed record CompiledRuleFragment(
    IReadOnlyList<(string Name, string Body)> AuxiliaryCtes,
    string MainCteBody,
    IReadOnlyList<NpgsqlParameter> Parameters);

/// <summary>
/// Compiles one loaded, already-validated <see cref="ParsedOwnershipRule"/> into SQL — dispatches
/// purely on the row's <c>RuleType</c>; no method here ever references a specific <c>RuleName</c>.
/// Every field name and edge tuple was already resolved through the fixed catalogs by
/// <see cref="RuleDefinitionValidator"/>, so this class trusts them. Comparisons never involve a
/// rule-supplied literal value (every leaf compares two column expressions), so no comparison
/// needs its own bound parameter — only hop <c>rel_type</c>/entity-type strings and numeric caps
/// do, and those are always bound, never concatenated.
/// </summary>
internal static class OwnershipRuleCompiler
{
    public static CompiledRuleFragment Compile(ParsedOwnershipRule rule, int ruleIndex) => rule.RuleType switch
    {
        OwnershipRuleType.FieldMatch => CompileFieldMatch(rule, ruleIndex),
        OwnershipRuleType.SiblingMatch => CompileSiblingMatch(rule, ruleIndex),
        OwnershipRuleType.InheritOwner => CompileInheritOwner(rule, ruleIndex),
        OwnershipRuleType.MajorityOwner => CompileMajorityOwner(rule, ruleIndex),
        _ => throw new InvalidOperationException($"Unreachable: rule_type '{rule.RuleType}' should have failed validation."),
    };

    // ================================================================== field_match

    private static CompiledRuleFragment CompileFieldMatch(ParsedOwnershipRule rule, int ruleIndex)
    {
        var ruleAlias = $"r{ruleIndex}";
        var def = rule.Definition;
        var parameters = new List<NpgsqlParameter>();

        string entityJoins;
        Func<string, string> resolveField;

        if (def.Via is { Count: > 0 } via)
        {
            var (chainJoins, idExpr, typeExpr, finalTypes) = BuildEdgeChain(via, rule.AppliesTo, ruleAlias, parameters);

            if (finalTypes.Count == 1)
            {
                var finalType = finalTypes[0];
                var table = EntityScopeCatalog.Resolve(finalType).Table;
                var ftAlias = $"{ruleAlias}ft";
                entityJoins = chainJoins + $"JOIN {table} {ftAlias} ON {ftAlias}.id = {idExpr}\n";
                resolveField = field => EntityFieldCatalog.Resolve(finalType, field, ftAlias)!;
            }
            else
            {
                // Polymorphic terminal (fan-in from more than one type): resolve the field via a
                // COALESCE of per-type correlated subqueries rather than joining every declared
                // type's table (which would multiply rows) — exactly one branch matches per row,
                // since typeExpr pins which type this particular row actually is.
                entityJoins = chainJoins;
                var perType = finalTypes.Select((t, i) =>
                {
                    var typeParam = $"{ruleAlias}ztype{i}";
                    parameters.Add(new NpgsqlParameter(typeParam, t));
                    return (Type: t, Table: EntityScopeCatalog.Resolve(t).Table, Alias: $"{ruleAlias}z{i}", TypeParam: typeParam);
                }).ToList();

                resolveField = field => "COALESCE(" + string.Join(", ", perType.Select(p =>
                    $"(SELECT {EntityFieldCatalog.Resolve(p.Type, field, p.Alias)} FROM {p.Table} {p.Alias} WHERE {p.Alias}.id = {idExpr} AND {typeExpr} = @{p.TypeParam})")) + ")";
            }
        }
        else
        {
            var table = EntityScopeCatalog.Resolve(rule.AppliesTo).Table;
            entityJoins = $"JOIN {table} e ON e.id = s.id\n";
            resolveField = field => EntityFieldCatalog.Resolve(rule.AppliesTo, field, "e")!;
        }

        var predicate = BuildFieldPredicate(resolveField, def.ResolvedEntityField!, def.ResolvedIdentityField!, def.How);
        if (def.And is { } and)
        {
            foreach (var condition in and)
            {
                predicate = $"({predicate} AND {BuildFieldPredicate(resolveField, condition.ResolvedEntityField!, condition.ResolvedIdentityField!, condition.How)})";
            }
        }

        string mainSql;
        if (def.Cap is { } cap)
        {
            var capParam = $"{ruleAlias}cap";
            parameters.Add(new NpgsqlParameter(capParam, cap));
            mainSql = $"""
                SELECT s.id AS entity_id, cand.id AS identity_id
                FROM scoped s
                {entityJoins}CROSS JOIN LATERAL (
                    SELECT i.id
                    FROM tenant.identity i
                    WHERE {predicate}
                    ORDER BY i.id
                    LIMIT @{capParam}
                ) AS cand
                """;
        }
        else
        {
            mainSql = $"""
                SELECT s.id AS entity_id, i.id AS identity_id
                FROM scoped s
                {entityJoins}JOIN tenant.identity i ON {predicate}
                """;
        }

        return new CompiledRuleFragment([], mainSql, parameters);
    }

    private static string BuildFieldPredicate(Func<string, string> resolveEntityField, string entityField, string identityField, string how) =>
        BuildComparison(resolveEntityField(entityField), IdentityFieldCatalog.Resolve(identityField, "i")!, how);

    // ================================================================== sibling_match

    private static CompiledRuleFragment CompileSiblingMatch(ParsedOwnershipRule rule, int ruleIndex)
    {
        var ruleAlias = $"r{ruleIndex}";
        var parameters = new List<NpgsqlParameter>();
        var table = EntityScopeCatalog.Resolve(rule.AppliesTo).Table;

        var left = EntityFieldCatalog.Resolve(rule.AppliesTo, rule.Definition.ResolvedEntityField!, "e")!;
        var right = EntityFieldCatalog.Resolve(rule.AppliesTo, rule.Definition.ResolvedIdentityField!, "sib")!;
        var predicate = BuildComparison(left, right, rule.Definition.How);

        var appliesToParam = $"{ruleAlias}appliesto";
        parameters.Add(new NpgsqlParameter(appliesToParam, rule.AppliesTo));

        var candidatesSql = $"""
            SELECT s.id AS entity_id, sib_pa.identity_id AS identity_id
            FROM scoped s
            JOIN {table} e ON e.id = s.id
            JOIN {table} sib ON sib.id <> s.id AND {predicate}
            JOIN tenant.party_assignment sib_pa
                ON sib_pa.entity_type = @{appliesToParam} AND sib_pa.entity_id = sib.id
               AND sib_pa.is_active = TRUE AND sib_pa.rank = 1
               {PriorOwnersOnly("sib_pa")}
            """;

        return new CompiledRuleFragment([], BuildMajorityAggregation(candidatesSql), parameters);
    }

    // ================================================================== inherit_owner

    private static CompiledRuleFragment CompileInheritOwner(ParsedOwnershipRule rule, int ruleIndex) =>
        rule.Definition.Via is { Count: > 0 } via
            ? CompileInheritOwnerVia(rule, ruleIndex, via)
            : CompileInheritOwnerWalk(rule, ruleIndex);

    /// <summary>
    /// Unlike <see cref="BuildEdgeChain"/> (used where fan-out should be preserved), each hop here
    /// picks exactly one target per source row via <c>LATERAL ... LIMIT 1</c> — <c>inherit_owner</c>
    /// always resolves to a single candidate per entity, with <c>prefer</c> (only meaningful on the
    /// last hop) breaking ties among multiple reachable targets.
    /// </summary>
    private static CompiledRuleFragment CompileInheritOwnerVia(ParsedOwnershipRule rule, int ruleIndex, IReadOnlyList<EdgeHop> hops)
    {
        var ruleAlias = $"r{ruleIndex}";
        var parameters = new List<NpgsqlParameter>();
        var idExpr = "s.id";
        var startTypeParam = $"{ruleAlias}starttype";
        parameters.Add(new NpgsqlParameter(startTypeParam, rule.AppliesTo));
        var typeExpr = $"@{startTypeParam}";

        var joins = new System.Text.StringBuilder();
        for (var i = 0; i < hops.Count; i++)
        {
            var hop = hops[i];
            var alias = $"{ruleAlias}h{i}";
            var relParam = $"{ruleAlias}rel{i}";
            parameters.Add(new NpgsqlParameter(relParam, hop.RelType));

            string edgePredicate, targetIdExpr, targetTypeExpr;
            if (hop.Direction == "out")
            {
                var toTypeParam = $"{ruleAlias}totype{i}";
                parameters.Add(new NpgsqlParameter(toTypeParam, hop.ToType));
                edgePredicate = $"he.from_id = {idExpr} AND he.from_type = {typeExpr} AND he.to_type = @{toTypeParam}";
                targetIdExpr = "he.to_id";
                targetTypeExpr = $"@{toTypeParam}";
            }
            else
            {
                var fromTypesParam = $"{ruleAlias}fromtypes{i}";
                parameters.Add(new NpgsqlParameter(fromTypesParam, hop.ResolvedFromTypes.ToArray()));
                edgePredicate = $"he.to_id = {idExpr} AND he.to_type = {typeExpr} AND he.from_type = ANY(@{fromTypesParam})";
                targetIdExpr = "he.from_id";
                targetTypeExpr = "he.from_type";
            }

            var orderBy = "he.id";
            if (i == hops.Count - 1 && rule.Definition.Prefer is { } prefer)
            {
                var keyParam = $"{ruleAlias}preferkey";
                var valueParam = $"{ruleAlias}prefervalue";
                parameters.Add(new NpgsqlParameter(keyParam, prefer.Prop));
                parameters.Add(new NpgsqlParameter(valueParam, prefer.Value.GetBoolean()));
                orderBy = $"((he.props::jsonb ->> @{keyParam})::boolean = @{valueParam}) DESC NULLS LAST, he.id";
            }

            joins.Append($"""
                CROSS JOIN LATERAL (
                    SELECT {targetIdExpr} AS target_id, {targetTypeExpr} AS target_type
                    FROM tenant.edge he
                    WHERE he.rel_type = @{relParam} AND he.is_deleted = FALSE AND {edgePredicate}
                    ORDER BY {orderBy}
                    LIMIT 1
                ) {alias}

                """);
            idExpr = $"{alias}.target_id";
            typeExpr = $"{alias}.target_type";
        }

        var mainSql = $"""
            SELECT s.id AS entity_id, pa.identity_id AS identity_id
            FROM scoped s
            {joins}JOIN tenant.party_assignment pa
                ON pa.entity_type = {typeExpr} AND pa.entity_id = {idExpr}
               AND pa.is_active = TRUE AND pa.rank = 1
               {PriorOwnersOnly("pa")}
            """;

        return new CompiledRuleFragment([], mainSql, parameters);
    }

    /// <summary>
    /// A walk rule's candidates are not computed here: <see cref="Walk.OwnershipWalkPropagator"/>
    /// has already found every entity's nearest owned ancestor for this run and staged it in
    /// <c>ownership_walk_candidate</c>, one row per (rule, entity), before the phase's first vote
    /// chunk ran. This joins the window to those rows. Relies on the builder binding <c>@runId</c>.
    ///
    /// <para>It replaces a <c>WITH RECURSIVE</c> walk from every scoped entity up to
    /// <c>maxDepth</c>, which expanded paths rather than nodes — no visited-set, so a group reachable
    /// two ways was expanded twice and diamond-heavy nesting grew exponentially — and walked past the
    /// nearest owned ancestor to the full depth before discarding everything but it.</para>
    /// </summary>
    private static CompiledRuleFragment CompileInheritOwnerWalk(ParsedOwnershipRule rule, int ruleIndex)
    {
        var walkRuleParam = $"r{ruleIndex}walkrule";

        var mainSql = $"""
            SELECT s.id AS entity_id, w.identity_id AS identity_id
            FROM scoped s
            JOIN tenant.ownership_walk_candidate w
                ON w.run_id = @runId AND w.rule_id = @{walkRuleParam} AND w.entity_id = s.id
            """;

        return new CompiledRuleFragment([], mainSql, [new NpgsqlParameter(walkRuleParam, rule.Id)]);
    }

    // ================================================================== majority_owner

    private static CompiledRuleFragment CompileMajorityOwner(ParsedOwnershipRule rule, int ruleIndex)
    {
        var ruleAlias = $"r{ruleIndex}";
        var parameters = new List<NpgsqlParameter>();
        var (joins, idExpr, typeExpr, _) = BuildEdgeChain(rule.Definition.Via!, rule.AppliesTo, ruleAlias, parameters);

        var candidatesSql = $"""
            SELECT s.id AS entity_id, pa.identity_id AS identity_id
            FROM scoped s
            {joins}JOIN tenant.party_assignment pa
                ON pa.entity_type = {typeExpr} AND pa.entity_id = {idExpr}
               AND pa.is_active = TRUE AND pa.rank = 1
               {PriorOwnersOnly("pa")}
            """;

        return new CompiledRuleFragment([], BuildMajorityAggregation(candidatesSql), parameters);
    }

    // ================================================================== shared helpers

    /// <summary>
    /// Keeps a rule reading only owners that existed before the current phase began: every prior
    /// run's, and this run's from earlier phases — never this run's from the phase being matched.
    ///
    /// <para>A phase used to be one query, which saw exactly that by construction. It is now many
    /// chunks writing as they go, and without this a chunk would see whatever the chunks before it
    /// in the same phase had just assigned — so which owner a sibling or a parent contributed would
    /// depend on which window happened to run first. Relies on the builder binding <c>@runId</c> and
    /// <c>@entityTypeParam</c> (the phase's entity type).</para>
    /// </summary>
    private static string PriorOwnersOnly(string alias) =>
        $"AND ({alias}.run_id <> @runId OR {alias}.entity_type <> @entityTypeParam)";

    /// <summary>
    /// Walks a hop chain from <paramref name="startType"/> preserving fan-out (plain <c>JOIN</c>s,
    /// not <c>LATERAL</c>) — used where every reachable target should become its own row, either
    /// as an independent candidate (<c>field_match</c>) or a vote to be tallied (<c>majority_owner</c>).
    /// Returns the final row(s)' id/type expressions and the set of types they can be (more than
    /// one only when the last hop fanned in from multiple source types).
    /// </summary>
    private static (string Joins, string IdExpr, string TypeExpr, IReadOnlyList<string> FinalTypes) BuildEdgeChain(
        IReadOnlyList<EdgeHop> hops, string startType, string ruleAlias, List<NpgsqlParameter> parameters)
    {
        var joins = new System.Text.StringBuilder();
        var idExpr = "s.id";
        var startTypeParam = $"{ruleAlias}cstarttype";
        parameters.Add(new NpgsqlParameter(startTypeParam, startType));
        var typeExpr = $"@{startTypeParam}";
        var currentTypes = new List<string> { startType };

        for (var i = 0; i < hops.Count; i++)
        {
            var hop = hops[i];
            var alias = $"{ruleAlias}c{i}";
            var relParam = $"{ruleAlias}crel{i}";
            parameters.Add(new NpgsqlParameter(relParam, hop.RelType));

            if (hop.Direction == "out")
            {
                var toType = hop.ToType!;
                var toTypeParam = $"{ruleAlias}ctotype{i}";
                parameters.Add(new NpgsqlParameter(toTypeParam, toType));
                joins.Append($"JOIN tenant.edge {alias} ON {alias}.rel_type = @{relParam} AND {alias}.is_deleted = FALSE AND {alias}.from_id = {idExpr} AND {alias}.from_type = {typeExpr} AND {alias}.to_type = @{toTypeParam}\n");
                idExpr = $"{alias}.to_id";
                typeExpr = $"@{toTypeParam}";
                currentTypes = [toType];
            }
            else
            {
                var fromTypes = hop.ResolvedFromTypes.ToArray();
                var fromTypesParam = $"{ruleAlias}cfromtypes{i}";
                parameters.Add(new NpgsqlParameter(fromTypesParam, fromTypes));
                joins.Append($"JOIN tenant.edge {alias} ON {alias}.rel_type = @{relParam} AND {alias}.is_deleted = FALSE AND {alias}.to_id = {idExpr} AND {alias}.to_type = {typeExpr} AND {alias}.from_type = ANY(@{fromTypesParam})\n");
                idExpr = $"{alias}.from_id";
                typeExpr = $"{alias}.from_type";
                currentTypes = fromTypes.ToList();
            }
        }

        return (joins.ToString(), idExpr, typeExpr, currentTypes);
    }

    /// <summary>Tallies duplicate (entity_id, identity_id) rows produced by fan-out and keeps only the most frequent identity per entity — deterministic tie-break by identity id.</summary>
    private static string BuildMajorityAggregation(string candidatesSql) => $"""
        SELECT DISTINCT ON (tallied.entity_id) tallied.entity_id, tallied.identity_id
        FROM (
            SELECT c.entity_id, c.identity_id, COUNT(*) AS votes
            FROM (
                {candidatesSql}
            ) c
            GROUP BY c.entity_id, c.identity_id
        ) tallied
        ORDER BY tallied.entity_id, tallied.votes DESC, tallied.identity_id
        """;

    private static string BuildComparison(string left, string right, string how) => how switch
    {
        RuleOperator.Exact =>
            $"({left} IS NOT NULL AND {left} <> '' AND {right} IS NOT NULL AND {right} <> '' AND {left} = {right})",
        RuleOperator.Contains =>
            $"({left} IS NOT NULL AND {right} IS NOT NULL AND {right} <> '' AND position({right} in {left}) > 0)",
        RuleOperator.StartsWith =>
            $"({left} IS NOT NULL AND {right} IS NOT NULL AND {right} <> '' AND left({left}, length({right})) = {right})",
        RuleOperator.EndsWith =>
            $"({left} IS NOT NULL AND {right} IS NOT NULL AND {right} <> '' AND right({left}, length({right})) = {right})",
        RuleOperator.Similar => BuildSimilarPredicate(left, right),
        _ => throw new InvalidOperationException($"Unreachable: how '{how}' should have failed validation."),
    };

    /// <summary>
    /// Delimiter-bounded prefix/suffix match (e.g. "jsmith_admin" is similar to "jsmith" — a
    /// delimiter-separated variant of the same base name — but "jsmithadmin" is not). Each
    /// compared value is regex-escaped before being embedded in a pattern, since it's row data
    /// being treated as a literal substring, never rule-author-supplied regex syntax.
    /// </summary>
    private static string BuildSimilarPredicate(string left, string right)
    {
        const string delimiterClass = "[-_$%#&!^(){}0-9.]";
        var escLeft = EscapeForRegex(left);
        var escRight = EscapeForRegex(right);

        return $"({left} IS NOT NULL AND {left} <> '' AND {right} IS NOT NULL AND {right} <> '' AND (" +
               $"{left} ~ ('^' || {escRight} || '{delimiterClass}') OR " +
               $"{right} ~ ('^' || {escLeft} || '{delimiterClass}') OR " +
               $"{left} ~ ('{delimiterClass}' || {escRight} || '$') OR " +
               $"{right} ~ ('{delimiterClass}' || {escLeft} || '$')" +
               "))";
    }

    /// <summary>Escapes a SQL expression's runtime value for safe embedding as a literal substring inside a Postgres regex pattern built via <c>||</c> concatenation.</summary>
    private static string EscapeForRegex(string expr) =>
        "regexp_replace(" + expr + @", '([.^$*+?()\[\]{}|\\])', '\\\1', 'g')";
}
