using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.Workflows.Ingestion.EdgeGate;

public sealed record EdgeResolutionResult(int Inserted, int Updated, int Removed);

/// <summary>
/// Connector-agnostic edge resolution (§7 of <c>ad_connector_ingestion.md</c>) — joins every
/// owner's <c>edge_refs</c> against every entity's <c>alias_keys</c> to form canonical edges.
/// account/grp/asset/entitlement are all treated uniformly as owner tables (see
/// <see cref="OwnerRefsSql"/>), even though only account/grp populate <c>edge_refs</c> today.
///
/// <see cref="ResolveFullAsync"/> (the initial scan) resolves every owner's *entire* edge_refs
/// footprint — nothing to diff against yet, so there's nothing narrower to do.
/// <see cref="ResolveIncrementalAsync"/> is delta-driven (the POC's "B7"): it resolves only the
/// individual refs a manifest actually added or removed (via <c>tenant.edge_ref_delta</c>,
/// populated by <see cref="EdgeRefDeltaComputer"/> before promotion), plus specific one-sided
/// refs that just healed — so a single membership change in a huge group costs proportional to
/// that one change, not to the group's whole membership. These are genuinely different shapes
/// (mirroring the POC's own separate <c>_resolve_initial</c>/<c>_resolve_manifest_b7</c>
/// functions), so they no longer share one internal resolve-to-staging method.
/// </summary>
public sealed class EdgeResolver(OneSidedRefIndex oneSidedRefIndex, ILogger<EdgeResolver> logger)
{
    private const string EmptyProps = "{}";

    /// <summary>Resolves edges for every account/grp/asset/entitlement — the initial (full) scan path.</summary>
    public async Task<EdgeResolutionResult> ResolveFullAsync(TenantDbContext db, Guid scanId, Guid scanManifestId, IReadOnlySet<string> twoSidedRelTypes)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        await SeedAllOwnersAsync(db);
        await oneSidedRefIndex.RefreshForSeedAsync(db, twoSidedRelTypes);

        await InsertOwnerScopedStagingRowsAsync(db, scanManifestId);
        var (inserted, updated) = await PromoteStagedEdgesAsync(db, scanId, scanManifestId);
        var removed = await ApplyOwnerScopedRemovalsAsync(db, scanId, scanManifestId);
        await CleanupAsync(db, scanManifestId);

        await transaction.CommitAsync();

        logger.LogInformation("Edge gate (full) for scan {ScanId}: {Inserted} inserted, {Updated} updated", scanId, inserted, updated);
        return new EdgeResolutionResult(inserted, updated, removed);
    }

    /// <summary>
    /// Resolves edges for the refs one manifest actually added/removed (§ B7), plus one-sided
    /// refs that just healed, including conservative both-sided removal.
    /// </summary>
    public async Task<EdgeResolutionResult> ResolveIncrementalAsync(TenantDbContext db, Guid scanId, Guid scanManifestId, IReadOnlySet<string> twoSidedRelTypes)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        await SeedChangedOwnersAsync(db, scanId, scanManifestId);
        await oneSidedRefIndex.RefreshForSeedAsync(db, twoSidedRelTypes);

        await InsertDeltaStagingRowsAsync(db, scanManifestId, scanManifestId);
        var (inserted, updated) = await PromoteStagedEdgesAsync(db, scanId, scanManifestId);
        var removed = await ApplyDeltaRemovalsAsync(db, scanId, scanManifestId, scanManifestId);
        await CleanupAsync(db, scanManifestId);
        await SqlExec.ExecAsync(db, "DELETE FROM tenant.edge_ref_delta WHERE scan_manifest_id = @scan_manifest_id",
            new NpgsqlParameter("scan_manifest_id", scanManifestId));

        await transaction.CommitAsync();

        logger.LogInformation(
            "Edge gate (incremental) for scan {ScanId}: {Inserted} inserted, {Updated} updated, {Removed} removed",
            scanId, inserted, updated, removed);
        return new EdgeResolutionResult(inserted, updated, removed);
    }

    private static async Task SeedAllOwnersAsync(TenantDbContext db)
    {
        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS seed_owner");
        await SqlExec.ExecAsync(db, "CREATE TEMP TABLE seed_owner (id uuid PRIMARY KEY, owner_type text NOT NULL)");

        var unionSql = string.Join("\nUNION ALL\n", OwnerRefsSql.OwnerTables.Select(t => $"SELECT id, '{t.EntityType}' FROM {t.Table}"));
        await SqlExec.ExecAsync(db, $"INSERT INTO seed_owner (id, owner_type)\n{unionSql}");
    }

    /// <summary>Owners whose content changed this manifest — used only to scope one-sided-ref refresh; B7 resolves edges from <c>edge_ref_delta</c>, not from this set.</summary>
    private static async Task SeedChangedOwnersAsync(TenantDbContext db, Guid scanId, Guid scanManifestId)
    {
        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS seed_owner");
        await SqlExec.ExecAsync(db, "CREATE TEMP TABLE seed_owner (id uuid PRIMARY KEY, owner_type text NOT NULL)");

        const string sql = """
            INSERT INTO seed_owner (id, owner_type)
            SELECT DISTINCT entity_id, entity_type
            FROM tenant.ingest_change_event
            WHERE scan_id = @scan_id AND scan_manifest_id = @scan_manifest_id
              AND entity_type IN ('account','grp','asset','entitlement')
            """;
        await SqlExec.ExecAsync(db, sql,
            new NpgsqlParameter("scan_id", scanId),
            new NpgsqlParameter("scan_manifest_id", scanManifestId));
    }

    /// <summary>Full resolve: every seeded owner's entire current edge_refs footprint, resolved against entity_alias.</summary>
    private static async Task InsertOwnerScopedStagingRowsAsync(TenantDbContext db, Guid gateManifestId)
    {
        var sql = $"""
            INSERT INTO tenant.staging_edge (scan_manifest_id, batch_seq, from_id, from_type, to_id, to_type, rel_type, props, content_hash, received_at)
            WITH owner_refs AS (
                {OwnerRefsSql.OwnerRefsCte("seed_owner")}
            ),
            {OwnerRefsSql.MatchedCte},
            deduped AS (
                SELECT DISTINCT ON (from_id, to_id, rel_type)
                    from_id, from_type, to_id, to_type, rel_type, COALESCE(props, '{EmptyProps}') AS props
                FROM matched
                ORDER BY from_id, to_id, rel_type
            )
            SELECT @gate_manifest_id, 0, from_id, from_type, to_id, to_type, rel_type, props,
                   {OwnerRefsSql.HashExpr}, now()
            FROM deduped
            """;
        await SqlExec.ExecAsync(db, sql, new NpgsqlParameter("gate_manifest_id", gateManifestId));
    }

    /// <summary>Incremental (B7) resolve: only refs added this manifest (via edge_ref_delta) plus refs that just healed via a one-sided park.</summary>
    private static async Task InsertDeltaStagingRowsAsync(TenantDbContext db, Guid scanManifestId, Guid gateManifestId)
    {
        var sql = $"""
            INSERT INTO tenant.staging_edge (scan_manifest_id, batch_seq, from_id, from_type, to_id, to_type, rel_type, props, content_hash, received_at)
            WITH owner_refs AS (
                {OwnerRefsSql.DeltaOwnerRefsCte("add")}
            ),
            {OwnerRefsSql.MatchedCte},
            {OwnerRefsSql.HealTargetAliasCte},
            {OwnerRefsSql.HealRefsCte},
            combined AS (
                SELECT from_id, from_type, to_id, to_type, rel_type, props FROM matched
                UNION ALL
                SELECT from_id, from_type, to_id, to_type, rel_type, props FROM heal_refs
            ),
            deduped AS (
                SELECT DISTINCT ON (from_id, to_id, rel_type)
                    from_id, from_type, to_id, to_type, rel_type, COALESCE(props, '{EmptyProps}') AS props
                FROM combined
                ORDER BY from_id, to_id, rel_type
            )
            SELECT @gate_manifest_id, 0, from_id, from_type, to_id, to_type, rel_type, props,
                   {OwnerRefsSql.HashExpr}, now()
            FROM deduped
            """;
        await SqlExec.ExecAsync(db, sql,
            new NpgsqlParameter("scan_manifest_id", scanManifestId),
            new NpgsqlParameter("gate_manifest_id", gateManifestId));
    }

    /// <summary>Promotes this run's staged edges into tenant.edge (hash-diffed upsert) and emits one ingest_change_event per actual insert/update. Shared by both resolve paths — identical regardless of how staging_edge got populated.</summary>
    private static async Task<(int Inserted, int Updated)> PromoteStagedEdgesAsync(TenantDbContext db, Guid scanId, Guid gateManifestId)
    {
        const string promoteSql = """
            WITH deduped AS (
                SELECT DISTINCT ON (from_id, to_id, rel_type)
                    from_id, from_type, to_id, to_type, rel_type, NULLIF(props, '{}') AS props, content_hash
                FROM tenant.staging_edge
                WHERE scan_manifest_id = @gate_manifest_id
                ORDER BY from_id, to_id, rel_type
            ),
            up AS (
                INSERT INTO tenant.edge (id, from_id, from_type, to_id, to_type, rel_type, props, content_hash, is_deleted, created_at, updated_at)
                SELECT gen_random_uuid(), from_id, from_type, to_id, to_type, rel_type, props, content_hash, FALSE, now(), now()
                FROM deduped
                ON CONFLICT (from_id, to_id, rel_type) DO UPDATE SET
                    from_type = excluded.from_type,
                    to_type = excluded.to_type,
                    props = excluded.props,
                    content_hash = excluded.content_hash,
                    is_deleted = FALSE,
                    updated_at = now()
                WHERE tenant.edge.content_hash IS DISTINCT FROM excluded.content_hash OR tenant.edge.is_deleted
                RETURNING id, (xmax = 0) AS is_insert
            ),
            ev AS (
                INSERT INTO tenant.ingest_change_event (id, scan_manifest_id, scan_id, entity_type, entity_id, change_type, occurred_at)
                SELECT gen_random_uuid(), @gate_manifest_id, @scan_id, 'edge', id,
                       CASE WHEN is_insert THEN 'inserted' ELSE 'updated' END, now()
                FROM up
                RETURNING 1
            )
            SELECT
                count(*) FILTER (WHERE is_insert)::int,
                count(*) FILTER (WHERE NOT is_insert)::int
            FROM up
            """;

        var (connection, transaction) = SqlExec.Conn(db);
        await using var command = new NpgsqlCommand(promoteSql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("gate_manifest_id", gateManifestId));
        command.Parameters.Add(new NpgsqlParameter("scan_id", scanId));
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    /// <summary>Full resolve's removal candidates: every live edge incident to a seeded owner not reproduced in this run's staging_edge.</summary>
    private static async Task<int> ApplyOwnerScopedRemovalsAsync(TenantDbContext db, Guid scanId, Guid gateManifestId)
    {
        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS cand_removal");
        const string candidateSql = """
            CREATE TEMP TABLE cand_removal AS
            SELECT e.id, e.from_id, e.to_id, e.rel_type
            FROM tenant.edge e
            JOIN seed_owner c ON c.id = e.from_id
            WHERE e.is_deleted = FALSE
              AND NOT EXISTS (
                  SELECT 1 FROM tenant.staging_edge s
                  WHERE s.scan_manifest_id = @gate_manifest_id
                    AND s.from_id = e.from_id AND s.to_id = e.to_id AND s.rel_type = e.rel_type)
            UNION
            SELECT e.id, e.from_id, e.to_id, e.rel_type
            FROM tenant.edge e
            JOIN seed_owner c ON c.id = e.to_id
            WHERE e.is_deleted = FALSE
              AND NOT EXISTS (
                  SELECT 1 FROM tenant.staging_edge s
                  WHERE s.scan_manifest_id = @gate_manifest_id
                    AND s.from_id = e.from_id AND s.to_id = e.to_id AND s.rel_type = e.rel_type)
            """;
        await SqlExec.ExecAsync(db, candidateSql, new NpgsqlParameter("gate_manifest_id", gateManifestId));

        return await VerifyAndApplyRemovalsAsync(db, scanId, gateManifestId);
    }

    /// <summary>B7 removal candidates: only the specific edge tuples this manifest's removed refs used to produce.</summary>
    private static async Task<int> ApplyDeltaRemovalsAsync(TenantDbContext db, Guid scanId, Guid scanManifestId, Guid gateManifestId)
    {
        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS cand_removal");
        var candidateSql = $"""
            CREATE TEMP TABLE cand_removal AS
            WITH owner_refs AS (
                {OwnerRefsSql.DeltaOwnerRefsCte("remove")}
            ),
            {OwnerRefsSql.MatchedCte}
            SELECT DISTINCT e.id, e.from_id, e.to_id, e.rel_type
            FROM matched m
            JOIN tenant.edge e ON e.from_id = m.from_id AND e.to_id = m.to_id AND e.rel_type = m.rel_type
            WHERE e.is_deleted = FALSE
              AND NOT EXISTS (
                  SELECT 1 FROM tenant.staging_edge s
                  WHERE s.scan_manifest_id = @gate_manifest_id
                    AND s.from_id = e.from_id AND s.to_id = e.to_id AND s.rel_type = e.rel_type)
            """;
        await SqlExec.ExecAsync(db, candidateSql,
            new NpgsqlParameter("scan_manifest_id", scanManifestId),
            new NpgsqlParameter("gate_manifest_id", gateManifestId));

        return await VerifyAndApplyRemovalsAsync(db, scanId, gateManifestId);
    }

    /// <summary>
    /// Conservative both-sided removal (§7.4), shared by both removal-candidate sources above: a
    /// candidate is only actually marked deleted if NEITHER of its endpoints' *current* refs still
    /// produce it — checked by re-deriving edges for just those candidates' endpoints (bounded),
    /// never for the whole graph. Assumes <c>cand_removal</c> already exists.
    /// </summary>
    private static async Task<int> VerifyAndApplyRemovalsAsync(TenantDbContext db, Guid scanId, Guid gateManifestId)
    {
        var candidateCount = await SqlExec.ScalarLongAsync(db, "SELECT count(*) FROM cand_removal");
        if (candidateCount == 0)
        {
            return 0;
        }

        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS cand_ep");
        await SqlExec.ExecAsync(db, "CREATE TEMP TABLE cand_ep AS SELECT from_id AS id FROM cand_removal UNION SELECT to_id FROM cand_removal");

        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS cand_produced");
        var producedSql = $"""
            CREATE TEMP TABLE cand_produced AS
            WITH owner_refs AS (
                {OwnerRefsSql.OwnerRefsCte("cand_ep")}
            ),
            {OwnerRefsSql.MatchedCte}
            SELECT DISTINCT from_id, to_id, rel_type FROM matched
            """;
        await SqlExec.ExecAsync(db, producedSql);

        const string deleteSql = """
            WITH del AS (
                UPDATE tenant.edge e SET is_deleted = TRUE, updated_at = now()
                FROM cand_removal cr
                WHERE e.id = cr.id
                  AND NOT EXISTS (
                      SELECT 1 FROM cand_produced p
                      WHERE p.from_id = cr.from_id AND p.to_id = cr.to_id AND p.rel_type = cr.rel_type)
                RETURNING e.id
            ),
            ev AS (
                INSERT INTO tenant.ingest_change_event (id, scan_manifest_id, scan_id, entity_type, entity_id, change_type, occurred_at)
                SELECT gen_random_uuid(), @gate_manifest_id, @scan_id, 'edge', id, 'deleted', now() FROM del
                RETURNING 1
            )
            SELECT count(*)::int FROM del
            """;

        var (connection, transaction) = SqlExec.Conn(db);
        await using var command = new NpgsqlCommand(deleteSql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("gate_manifest_id", gateManifestId));
        command.Parameters.Add(new NpgsqlParameter("scan_id", scanId));
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task CleanupAsync(TenantDbContext db, Guid gateManifestId)
    {
        await SqlExec.ExecAsync(db, "DELETE FROM tenant.staging_edge WHERE scan_manifest_id = @gate_manifest_id", new NpgsqlParameter("gate_manifest_id", gateManifestId));
        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS seed_owner");
        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS cand_removal");
        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS cand_ep");
        await SqlExec.ExecAsync(db, "DROP TABLE IF EXISTS cand_produced");
    }
}
