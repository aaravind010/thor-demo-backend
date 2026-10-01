namespace Thor.Workflows.Ingestion.EdgeGate;

/// <summary>
/// Shared SQL fragments for treating account/grp/asset/entitlement uniformly as "owner" tables
/// whose rows may declare <c>edge_refs</c> (§7) — mirrors the POC's <c>_OWNER_TABLES</c>
/// (<c>poc/ingest/edge_gate.py</c>). No connector populates <c>edge_refs</c> on an asset or
/// entitlement row today (CyberArk's Safe/Asset and the entitlement infrastructure are edge
/// *targets* only) — included anyway so the join is ready the moment one does, no code change
/// needed then. Used by both <see cref="EdgeResolver"/> (resolving edges) and
/// <see cref="OneSidedRefIndex"/> (parking one-sided refs).
/// </summary>
public static class OwnerRefsSql
{
    public static readonly (string Table, string EntityType)[] OwnerTables =
    [
        ("tenant.account", "account"),
        ("tenant.grp", "grp"),
        ("tenant.asset", "asset"),
        ("tenant.entitlement", "entitlement"),
    ];

    /// <summary>
    /// UNION ALL of each owner table's unnested <c>edge_refs</c>, scoped to the ids in
    /// <paramref name="seedTable"/> (a temp table with an <c>id</c> column). <c>raw_attributes</c>
    /// is TEXT (no C# model has a <c>jsonb</c> column annotation), hence the explicit
    /// <c>::jsonb</c> cast — a NULL/absent <c>EdgeRefs</c> array safely yields zero rows via the
    /// COALESCE, not an error, regardless of what shape a given owner type's raw_attributes has.
    /// </summary>
    public static string OwnerRefsCte(string seedTable) =>
        string.Join("\nUNION ALL\n", OwnerTables.Select(t => $"""
            SELECT o.id AS owner_id, '{t.EntityType}'::text AS owner_type, o.source_id, o.connector_type,
                   r->>'Rel' AS rel, r->>'Dir' AS dir, r->>'Key' AS key, r->>'TargetType' AS target_type,
                   r->>'Props' AS props
            FROM {t.Table} o
            JOIN {seedTable} sc ON sc.id = o.id
            CROSS JOIN LATERAL jsonb_array_elements(COALESCE(o.raw_attributes::jsonb->'EdgeRefs', '[]'::jsonb)) AS r
            """));

    /// <summary>
    /// Resolves an <c>owner_refs</c> row (from <see cref="OwnerRefsCte"/>) against
    /// <c>tenant.entity_alias</c> into an edge tuple. Owner/alias <c>source_id</c> are
    /// non-nullable Guid columns on every table involved, so a plain <c>=</c> is correct here
    /// (unlike the POC's <c>IS NOT DISTINCT FROM</c>, kept there only because it wasn't certain
    /// owner source_id was always non-null).
    /// </summary>
    public const string MatchedCte = """
        matched AS (
            SELECT
                CASE WHEN orf.dir = 'out' THEN orf.owner_id   ELSE al.entity_id   END AS from_id,
                CASE WHEN orf.dir = 'out' THEN orf.owner_type ELSE al.entity_type END AS from_type,
                CASE WHEN orf.dir = 'out' THEN al.entity_id   ELSE orf.owner_id   END AS to_id,
                CASE WHEN orf.dir = 'out' THEN al.entity_type ELSE orf.owner_type END AS to_type,
                orf.rel AS rel_type,
                orf.props AS props
            FROM owner_refs orf
            JOIN tenant.entity_alias al
              ON al.alias_key = orf.key
             AND al.connector_type = orf.connector_type
             AND al.source_id = orf.source_id
             AND (orf.target_type IS NULL OR al.entity_type = orf.target_type)
        )
        """;

    /// <summary>
    /// UNION ALL of each owner table's <c>edge_ref_delta</c> rows for one <paramref name="op"/>
    /// ("add"/"remove"), joined back to canonical by <c>(source_id, native_id)</c> — the B7
    /// per-*ref* analog of <see cref="OwnerRefsCte"/>, which unnests an owner's *entire* current
    /// <c>edge_refs</c> array. Same output shape as <see cref="OwnerRefsCte"/>, so it feeds
    /// <see cref="MatchedCte"/> unchanged. Scoped by the caller's <c>@scan_manifest_id</c>
    /// parameter; <paramref name="op"/> is always a C# literal ("add"/"remove"), never external
    /// input, so inlining it is safe.
    /// </summary>
    public static string DeltaOwnerRefsCte(string op) =>
        string.Join("\nUNION ALL\n", OwnerTables.Select(t => $"""
            SELECT o.id AS owner_id, '{t.EntityType}'::text AS owner_type, o.source_id, o.connector_type,
                   d.ref::jsonb->>'Rel' AS rel, d.ref::jsonb->>'Dir' AS dir, d.ref::jsonb->>'Key' AS key,
                   d.ref::jsonb->>'TargetType' AS target_type, d.ref::jsonb->>'Props' AS props
            FROM tenant.edge_ref_delta d
            JOIN {t.Table} o ON o.source_id = d.source_id AND o.native_id = d.native_id
            WHERE d.scan_manifest_id = @scan_manifest_id AND d.op = '{op}' AND d.entity_type = '{t.EntityType}'
            """));

    /// <summary>
    /// Entities changed this manifest, expanded to their alias keys — the "did a healable
    /// one-sided ref's target just arrive/change" side of B7 healing (mirrors the POC's
    /// <c>heal_tgt_alias</c>). Scoped by <c>@scan_manifest_id</c>.
    /// </summary>
    public const string HealTargetAliasCte = """
        heal_target_alias AS (
            SELECT al.alias_key AS key, al.connector_type, al.source_id, al.entity_id, al.entity_type
            FROM tenant.entity_alias al
            JOIN tenant.ingest_change_event ce ON ce.entity_id = al.entity_id
            WHERE ce.scan_manifest_id = @scan_manifest_id
              AND ce.entity_type IN ('account','grp','asset','entitlement')
        )
        """;

    /// <summary>
    /// One-sided refs whose target just healed via <see cref="HealTargetAliasCte"/> — resolves
    /// directly into edge tuples (both endpoints are already known), unlike
    /// <see cref="OwnerRefsCte"/>/<see cref="MatchedCte"/>'s two-step shape. Depends on
    /// <see cref="HealTargetAliasCte"/> being included in the same statement.
    /// </summary>
    public const string HealRefsCte = """
        heal_refs AS (
            SELECT
                CASE WHEN osr.dir = 'out' THEN osr.owner_id   ELSE hta.entity_id   END AS from_id,
                CASE WHEN osr.dir = 'out' THEN osr.owner_type ELSE hta.entity_type END AS from_type,
                CASE WHEN osr.dir = 'out' THEN hta.entity_id   ELSE osr.owner_id   END AS to_id,
                CASE WHEN osr.dir = 'out' THEN hta.entity_type ELSE osr.owner_type END AS to_type,
                osr.rel AS rel_type,
                osr.props AS props
            FROM tenant.one_sided_ref osr
            JOIN heal_target_alias hta
              ON hta.key = osr.key AND hta.connector_type = osr.connector_type
             AND hta.source_id IS NOT DISTINCT FROM osr.source_id
             AND (osr.target_type IS NULL OR hta.entity_type = osr.target_type)
        )
        """;

    /// <summary>
    /// SHA-256 content hash, computed entirely in SQL so no row round-trips into C# just to be
    /// hashed and reinserted — byte-for-byte pinned to
    /// <see cref="Hashing.ContentHasher.EdgeHash"/> (see <c>EdgeHashParityTests</c>). Deliberately
    /// string concatenation, not <c>jsonb</c> construction: <c>props</c> is plain TEXT end to end
    /// (never cast to <c>jsonb</c>), so the hash never has to reproduce jsonb's object-key
    /// reordering to stay in sync with the C# side.
    /// </summary>
    public const string HashExpr =
        "encode(sha256(convert_to(" +
        "'[\"' || from_id::text || '\", \"' || to_id::text || '\", \"' || rel_type || '\", ' || COALESCE(props, '{}') || ']', " +
        "'UTF8')), 'hex')";
}
