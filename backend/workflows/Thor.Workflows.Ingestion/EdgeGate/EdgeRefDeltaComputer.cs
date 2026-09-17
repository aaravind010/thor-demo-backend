using Npgsql;
using Thor.DataLayer.Data;

namespace Thor.Workflows.Ingestion.EdgeGate;

/// <summary>
/// Computes, per owner table, which individual <c>edge_refs</c> entries a manifest added or
/// removed — the B7 prepass (<c>poc/ingest/reducer.py</c>'s <c>stage_edge_ref_deltas</c>) that
/// lets an incremental edge resolve touch only the refs that actually changed, not a changed
/// owner's *entire* membership footprint.
///
/// Must run BEFORE <c>Promoter</c>'s upsert overwrites canonical <c>raw_attributes</c> for this
/// manifest — the diff needs canonical's OLD row, read in the same transaction, one step ahead of
/// the write that replaces it. Deliberately skips the POC's <c>entity_edge_refs</c> baseline and
/// <c>edge_changed_key</c> pre-filter tables: those exist only because the POC's diff runs in
/// Python across two separate passes; reading canonical's current row directly, in the same SQL
/// statement, makes both unnecessary here. <c>jsonb</c>'s <c>=</c> operator compares by
/// decomposed structure (not text), so the add/remove diff below is naturally order/key
/// independent — no separate ref-hash needed either.
///
/// Must NOT run on the initial/full scan — canonical is empty then, so every ref would look like
/// an "add", and <see cref="EdgeResolver.ResolveFullAsync"/> never reads <c>edge_ref_delta</c>
/// anyway. Callers guard this with the same <c>ScanLifecycle.IsInitialScanAsync</c> check used to
/// choose between <see cref="EdgeResolver.ResolveFullAsync"/>/<see cref="EdgeResolver.ResolveIncrementalAsync"/>.
/// </summary>
public sealed class EdgeRefDeltaComputer
{
    /// <summary>
    /// Diffs this manifest's staged <c>edge_refs</c> against canonical for one owner table,
    /// writing the add/remove rows to <c>tenant.edge_ref_delta</c>. Leads with a delete-by-manifest
    /// so a retried invocation (Step Functions may retry a step) doesn't double-insert.
    /// </summary>
    public async Task ComputeAsync(TenantDbContext db, Guid scanManifestId, string ownerTable, string entityType)
    {
        await SqlExec.ExecAsync(db,
            "DELETE FROM tenant.edge_ref_delta WHERE scan_manifest_id = @scan_manifest_id AND entity_type = @entity_type",
            new NpgsqlParameter("scan_manifest_id", scanManifestId),
            new NpgsqlParameter("entity_type", entityType));

        var stagingTable = $"tenant.staging_{entityType}";
        var sql = $"""
            WITH staged AS (
                SELECT DISTINCT ON (source_id, native_id) source_id, native_id,
                       raw_attributes::jsonb->'EdgeRefs' AS new_refs
                FROM {stagingTable}
                WHERE scan_manifest_id = @scan_manifest_id
                ORDER BY source_id, native_id, received_at DESC, batch_seq DESC
            ),
            paired AS (
                SELECT s.source_id, s.native_id,
                       COALESCE(s.new_refs, '[]'::jsonb) AS new_refs,
                       COALESCE(o.raw_attributes::jsonb->'EdgeRefs', '[]'::jsonb) AS old_refs
                FROM staged s
                LEFT JOIN {ownerTable} o ON o.source_id = s.source_id AND o.native_id = s.native_id
            ),
            added AS (
                SELECT p.source_id, p.native_id, @entity_type AS entity_type, 'add' AS op, n.ref
                FROM paired p
                CROSS JOIN LATERAL jsonb_array_elements(p.new_refs) AS n(ref)
                WHERE NOT EXISTS (
                    SELECT 1 FROM jsonb_array_elements(p.old_refs) AS o(ref) WHERE o.ref = n.ref)
            ),
            removed AS (
                SELECT p.source_id, p.native_id, @entity_type AS entity_type, 'remove' AS op, o.ref
                FROM paired p
                CROSS JOIN LATERAL jsonb_array_elements(p.old_refs) AS o(ref)
                WHERE NOT EXISTS (
                    SELECT 1 FROM jsonb_array_elements(p.new_refs) AS n(ref) WHERE n.ref = o.ref)
            )
            INSERT INTO tenant.edge_ref_delta (id, scan_manifest_id, source_id, native_id, entity_type, op, ref)
            SELECT gen_random_uuid(), @scan_manifest_id, source_id, native_id, entity_type, op, ref::text FROM added
            UNION ALL
            SELECT gen_random_uuid(), @scan_manifest_id, source_id, native_id, entity_type, op, ref::text FROM removed
            """;

        await SqlExec.ExecAsync(db, sql,
            new NpgsqlParameter("scan_manifest_id", scanManifestId),
            new NpgsqlParameter("entity_type", entityType));
    }
}
