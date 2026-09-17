using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;

namespace Thor.Workflows.Ingestion.Promotion;

public sealed record PromotionResult(int Inserted, int Updated);

/// <summary>
/// Promotes staged rows for one scan manifest into their canonical tables — a set-based,
/// content-hash-diffed upsert, since there's
/// no LINQ translation for <c>INSERT ... ON CONFLICT ... WHERE ... RETURNING xmax = 0</c>.
/// Also maintains <see cref="EntityAlias"/> rows for every promoted entity (the alias lookup
/// the edge gate depends on, §7.1) and emits one <see cref="IngestChangeEvent"/> per actual
/// insert/update — unchanged rows (same content hash) produce neither.
/// </summary>
public sealed class Promoter(IEntityAliasRepository entityAliases, ILogger<Promoter> logger)
{
    private static readonly Guid UnclassifiedAccountTypeId = WellKnownAccountTypes.Unclassified;

    public async Task<PromotionResult> PromoteAccountsAsync(TenantDbContext db, Guid scanManifestId, Guid scanId)
    {
        const string sql = """
            WITH deduped AS (
                SELECT DISTINCT ON (source_id, native_id) *
                FROM tenant.staging_account
                WHERE scan_manifest_id = @scan_manifest_id
                ORDER BY source_id, native_id, received_at DESC, batch_seq DESC
            )
            INSERT INTO tenant.account (
                id, source_id, connector_type, native_id, account_kind, is_human, display_name,
                sam_account_name, upn, email, domain_name, filer_name, native_account_id,
                is_deleted, is_disabled, account_type_id, raw_attributes, content_hash,
                hash_version, created_at, updated_at
            )
            SELECT gen_random_uuid(), source_id, connector_type, native_id, account_kind, is_human,
                   display_name, sam_account_name, upn, email, domain_name, filer_name,
                   native_account_id, is_deleted, is_disabled, @unclassified_account_type_id,
                   raw_attributes, content_hash, 0, now(), now()
            FROM deduped
            ON CONFLICT (source_id, native_id) DO UPDATE SET
                account_kind = excluded.account_kind,
                is_human = excluded.is_human,
                display_name = excluded.display_name,
                sam_account_name = excluded.sam_account_name,
                upn = excluded.upn,
                email = excluded.email,
                domain_name = excluded.domain_name,
                filer_name = excluded.filer_name,
                native_account_id = excluded.native_account_id,
                is_deleted = excluded.is_deleted,
                is_disabled = excluded.is_disabled,
                raw_attributes = excluded.raw_attributes,
                content_hash = excluded.content_hash,
                updated_at = now()
            WHERE tenant.account.content_hash IS DISTINCT FROM excluded.content_hash
            RETURNING id, source_id, connector_type, raw_attributes, (xmax = 0) AS inserted
            """;

        return await PromoteAsync(db, "account", sql,
            [
                new NpgsqlParameter("scan_manifest_id", scanManifestId),
                new NpgsqlParameter("unclassified_account_type_id", UnclassifiedAccountTypeId),
            ],
            "staging_account", scanManifestId, scanId);
    }

    public async Task<PromotionResult> PromoteGroupsAsync(TenantDbContext db, Guid scanManifestId, Guid scanId)
    {
        const string sql = """
            WITH deduped AS (
                SELECT DISTINCT ON (source_id, native_id) *
                FROM tenant.staging_grp
                WHERE scan_manifest_id = @scan_manifest_id
                ORDER BY source_id, native_id, received_at DESC, batch_seq DESC
            )
            INSERT INTO tenant.grp (
                id, source_id, connector_type, native_id, group_class, display_name, email,
                domain_name, is_large_group, is_deleted, raw_attributes, content_hash,
                hash_version, created_at, updated_at
            )
            SELECT gen_random_uuid(), source_id, connector_type, native_id, group_class,
                   display_name, email, domain_name, is_large_group, is_deleted,
                   raw_attributes, content_hash, 0, now(), now()
            FROM deduped
            ON CONFLICT (source_id, native_id) DO UPDATE SET
                group_class = excluded.group_class,
                display_name = excluded.display_name,
                email = excluded.email,
                domain_name = excluded.domain_name,
                is_large_group = excluded.is_large_group,
                is_deleted = excluded.is_deleted,
                raw_attributes = excluded.raw_attributes,
                content_hash = excluded.content_hash,
                updated_at = now()
            WHERE tenant.grp.content_hash IS DISTINCT FROM excluded.content_hash
            RETURNING id, source_id, connector_type, raw_attributes, (xmax = 0) AS inserted
            """;

        return await PromoteAsync(db, "grp", sql,
            [new NpgsqlParameter("scan_manifest_id", scanManifestId)],
            "staging_grp", scanManifestId, scanId);
    }

    public async Task<PromotionResult> PromoteAssetsAsync(TenantDbContext db, Guid scanManifestId, Guid scanId)
    {
        const string sql = """
            WITH deduped AS (
                SELECT DISTINCT ON (source_id, native_id) *
                FROM tenant.staging_asset
                WHERE scan_manifest_id = @scan_manifest_id
                ORDER BY source_id, native_id, received_at DESC, batch_seq DESC
            )
            INSERT INTO tenant.asset (
                id, source_id, connector_type, native_id, asset_type, display_name, full_path,
                filer_name, file_size, file_count, broken_acl, is_protected, parent_asset_id,
                raw_attributes, content_hash, hash_version, created_at, updated_at
            )
            SELECT gen_random_uuid(), source_id, connector_type, native_id, asset_type,
                   display_name, full_path, filer_name, file_size, file_count, broken_acl,
                   is_protected, NULL, raw_attributes, content_hash, 0, now(), now()
            FROM deduped
            ON CONFLICT (source_id, native_id) DO UPDATE SET
                asset_type = excluded.asset_type,
                display_name = excluded.display_name,
                full_path = excluded.full_path,
                filer_name = excluded.filer_name,
                file_size = excluded.file_size,
                file_count = excluded.file_count,
                broken_acl = excluded.broken_acl,
                is_protected = excluded.is_protected,
                raw_attributes = excluded.raw_attributes,
                content_hash = excluded.content_hash,
                updated_at = now()
            WHERE tenant.asset.content_hash IS DISTINCT FROM excluded.content_hash
            RETURNING id, source_id, connector_type, raw_attributes, (xmax = 0) AS inserted
            """;

        return await PromoteAsync(db, "asset", sql,
            [new NpgsqlParameter("scan_manifest_id", scanManifestId)],
            "staging_asset", scanManifestId, scanId);
    }

    public async Task<PromotionResult> PromoteEntitlementsAsync(TenantDbContext db, Guid scanManifestId, Guid scanId)
    {
        const string sql = """
            WITH deduped AS (
                SELECT DISTINCT ON (source_id, native_id) *
                FROM tenant.staging_entitlement
                WHERE scan_manifest_id = @scan_manifest_id
                ORDER BY source_id, native_id, received_at DESC, batch_seq DESC
            )
            INSERT INTO tenant.entitlement (
                id, source_id, connector_type, native_id, entitlement_type, name, description,
                is_admin, scope, instance_name, sudo_path, sudo_host, raw_attributes,
                content_hash, hash_version, created_at, updated_at
            )
            SELECT gen_random_uuid(), source_id, connector_type, native_id, entitlement_type,
                   name, description, is_admin, scope, instance_name, sudo_path, sudo_host,
                   raw_attributes, content_hash, 0, now(), now()
            FROM deduped
            ON CONFLICT (source_id, native_id) DO UPDATE SET
                entitlement_type = excluded.entitlement_type,
                name = excluded.name,
                description = excluded.description,
                is_admin = excluded.is_admin,
                scope = excluded.scope,
                instance_name = excluded.instance_name,
                sudo_path = excluded.sudo_path,
                sudo_host = excluded.sudo_host,
                raw_attributes = excluded.raw_attributes,
                content_hash = excluded.content_hash,
                updated_at = now()
            WHERE tenant.entitlement.content_hash IS DISTINCT FROM excluded.content_hash
            RETURNING id, source_id, connector_type, raw_attributes, (xmax = 0) AS inserted
            """;

        return await PromoteAsync(db, "entitlement", sql,
            [new NpgsqlParameter("scan_manifest_id", scanManifestId)],
            "staging_entitlement", scanManifestId, scanId);
    }

    private async Task<PromotionResult> PromoteAsync(
        TenantDbContext db, string entityType, string upsertSql, NpgsqlParameter[] upsertParameters,
        string stagingTable, Guid scanManifestId, Guid scanId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        var promoted = await ExecuteUpsertAsync(db, upsertSql, upsertParameters);

        var now = DateTimeOffset.UtcNow;
        var inserted = 0;
        var updated = 0;
        foreach (var row in promoted)
        {
            inserted += row.Inserted ? 1 : 0;
            updated += row.Inserted ? 0 : 1;

            db.IngestChangeEvents.Add(new IngestChangeEvent
            {
                Id = Guid.NewGuid(),
                ScanManifestId = scanManifestId,
                ScanId = scanId,
                EntityType = entityType,
                EntityId = row.Id,
                ChangeType = row.Inserted ? "inserted" : "updated",
                OccurredAt = now,
            });

            await entityAliases.ReplaceForEntityAsync(
                entityType, row.Id, row.SourceId, row.ConnectorType, ExtractAliasKeys(row.RawAttributes));
        }

        await db.SaveChangesAsync();

        // stagingTable is always one of a handful of internal constants (never external input),
        // so building it into the SQL text is safe — table names can't be parameterized as
        // values, unlike scan_manifest_id below, which is a genuine ExecuteSqlRawAsync
        // positional parameter.
#pragma warning disable EF1003
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM tenant." + stagingTable + " WHERE scan_manifest_id = {0}",
            [scanManifestId]);
#pragma warning restore EF1003

        await transaction.CommitAsync();

        logger.LogInformation(
            "Promoted {EntityType}(s) for scan manifest {ScanManifestId}: {Inserted} inserted, {Updated} updated",
            entityType, scanManifestId, inserted, updated);

        return new PromotionResult(inserted, updated);
    }

    // Upserts a whole batch (up to ~1M+ rows) in one statement and streams the RETURNING set
    // back in one round trip — legitimately slower than Npgsql's 30s default command timeout.
    private const int UpsertCommandTimeoutSeconds = 300;

    private static async Task<List<PromotedRow>> ExecuteUpsertAsync(
        TenantDbContext db, string sql, NpgsqlParameter[] parameters)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = UpsertCommandTimeoutSeconds,
        };
        command.Parameters.AddRange(parameters);

        var rows = new List<PromotedRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PromotedRow(
                Id: reader.GetGuid(0),
                SourceId: reader.GetGuid(1),
                ConnectorType: reader.GetFieldValue<short>(2),
                RawAttributes: reader.GetString(3),
                Inserted: reader.GetBoolean(4)));
        }

        return rows;
    }

    private static IReadOnlyList<string> ExtractAliasKeys(string rawAttributesJson)
    {
        var view = JsonSerializer.Deserialize<RawAttributesAliasKeysView>(rawAttributesJson);
        return view?.AliasKeys ?? [];
    }

    private sealed record PromotedRow(Guid Id, Guid SourceId, short ConnectorType, string RawAttributes, bool Inserted);

    private sealed record RawAttributesAliasKeysView(IReadOnlyList<string>? AliasKeys);
}
