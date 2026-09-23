using Npgsql;
using NpgsqlTypes;
using Thor.DataLayer.Data;

namespace Thor.Workflows.Ingestion.EdgeGate;

/// <summary>
/// Maintains the <see cref="Thor.DataLayer.Models.Tenants.OneSidedRef"/> reverse index (§7.3,
/// one-sided reference healing) — the park-and-heal mechanism for relationship types stored on
/// only one endpoint (e.g. AD's <c>manager</c>/<c>managedBy</c>), so a target arriving in a later
/// manifest still resolves the ref without re-scanning every owner.
///
/// Set-based SQL against the run's <c>seed_owner</c> temp table (built by
/// <see cref="EdgeResolver"/>) rather than a per-owner EF delete+insert loop — mirrors the POC's
/// <c>_build_onesided</c>/<c>_refresh_onesided</c>. The same delete-by-seed/insert-scoped-to-seed
/// shape serves both the full resolve (seed = every owner) and the incremental resolve (seed =
/// owners whose content changed this manifest) — there is no separate "rebuild everything" path.
/// </summary>
public sealed class OneSidedRefIndex
{
    /// <summary>Replaces every seeded owner's parked one-sided refs with its current set.</summary>
    public async Task RefreshForSeedAsync(TenantDbContext db, IReadOnlySet<string> twoSidedRelTypes, CancellationToken cancellationToken = default)
    {
        await SqlExec.ExecAsync(db, "DELETE FROM tenant.one_sided_ref WHERE owner_id IN (SELECT id FROM seed_owner)", cancellationToken);

        var sql = $"""
            INSERT INTO tenant.one_sided_ref (id, key, owner_id, owner_type, rel, dir, target_type, props, source_id, connector_type, created_at)
            {OneSidedInsertSql("seed_owner")}
            """;
        var twoSided = new NpgsqlParameter("two_sided", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = twoSidedRelTypes.ToArray(),
        };
        await SqlExec.ExecAsync(db, sql, cancellationToken, twoSided);
    }

    private static string OneSidedInsertSql(string seedTable) =>
        string.Join("\nUNION ALL\n", OwnerRefsSql.OwnerTables.Select(t => $"""
            SELECT gen_random_uuid(), r->>'Key', o.id, '{t.EntityType}'::text, r->>'Rel', r->>'Dir', r->>'TargetType', r->>'Props',
                   o.source_id, o.connector_type, now()
            FROM {t.Table} o
            JOIN {seedTable} sc ON sc.id = o.id
            CROSS JOIN LATERAL jsonb_array_elements(COALESCE(o.raw_attributes::jsonb->'EdgeRefs', '[]'::jsonb)) AS r
            WHERE NOT (r->>'Rel' = ANY(@two_sided))
            """));
}
