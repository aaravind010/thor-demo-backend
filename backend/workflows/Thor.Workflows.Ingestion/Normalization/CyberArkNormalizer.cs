using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.Extraction;
using Thor.Workflows.Ingestion.Hashing;
using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// Normalizes CyberArk's raw export (one clean JSON document — no multi-document quirks like
/// AD's, though <see cref="CyberArkExtractor"/> still recovers malformed root JSON the same way
/// AD does) into privileged accounts, safes, and safe-membership edges.
///
/// - <c>Accounts.AccountDetails</c> → <see cref="ParsedAccount"/> (the managed/privileged
///   credentials CyberArk safeguards), via <c>cyberark-account.json</c>. Each declares a
///   <c>STORED_IN</c> edge to the safe (<c>SafeName</c>, resolved to that safe's <c>SafeNumber</c>)
///   it lives in.
/// - <c>Safes</c> → <see cref="ParsedAsset"/> (a governed resource, not an account or group), via
///   <c>cyberark-safe.json</c>. A safe never declares its own outbound edges — it's only ever an
///   edge target.
/// - <c>Members</c> (safe-permission grants) — structurally CyberArk's version of Unix's
///   <c>PublicFolderPermission</c>: edge-level properties on a <c>HAS_ACCESS</c> edge, not a
///   standalone Entitlement. Each distinct <c>(MemberType, MemberName)</c> becomes its own
///   CyberArk-scoped <see cref="ParsedAccount"/> (<c>MemberType: "User"</c>) or
///   <see cref="ParsedGroup"/> (<c>"Group"</c>) — CyberArk's own privileged-credential Accounts are
///   a completely different principal space (managed secrets, not vault users), confirmed to have
///   zero identity overlap — no cross-connector identity resolution is attempted here, that's
///   ATRE's job downstream. Every safe a member has access to becomes one <c>HAS_ACCESS</c> edge
///   (props = that safe's <c>Permissions</c> object plus a derived <c>is_admin</c> flag) — a member
///   is consolidated across every <c>Members[]</c> row mentioning them before being staged once,
///   so their full set of safe-access edges survives promotion's per-manifest dedup instead of
///   only the last-staged row's single edge surviving.
///
/// <c>Safes</c>/<c>Platforms</c> beyond the above are read but not further normalized — no clean
/// Account/Group/Asset/Entitlement/Edge fit for policy-template metadata (<c>Platforms</c>).
/// </summary>
public sealed class CyberArkNormalizer(ILogger<CyberArkNormalizer> logger, IAttributeMapProvider mapProvider) : IConnectorNormalizer
{
    public IngestBatch Normalize(byte[] rawExportBytes, Guid sourceId)
    {
        var accountMap = mapProvider.GetMap(ConnectorTypes.CyberArk, "Account");
        var safeMap = mapProvider.GetMap(ConnectorTypes.CyberArk, "Asset");
        var memberAccountMap = mapProvider.GetMap(ConnectorTypes.CyberArk, "MemberAccount");
        var memberGroupMap = mapProvider.GetMap(ConnectorTypes.CyberArk, "MemberGroup");

        var export = CyberArkExtractor.Extract(rawExportBytes);
        var root = export.Root;

        var (assets, safeNameToNumber, droppedSafes) = BuildSafes(root, sourceId, safeMap);

        var accounts = new List<ParsedAccount>();
        var droppedAccounts = 0;
        if (root["Accounts"] is JsonObject accountsNode && accountsNode["AccountDetails"] is JsonArray accountDetails)
        {
            foreach (var node in accountDetails)
            {
                if (node is not JsonObject raw)
                {
                    continue;
                }

                var nativeId = MappedFieldReader.AsString(raw["AccountId"]);
                if (string.IsNullOrEmpty(nativeId))
                {
                    droppedAccounts++;
                    continue;
                }

                accounts.Add(BuildPrivilegedAccount(raw, nativeId, sourceId, accountMap, safeNameToNumber));
            }
        }

        var (memberAccounts, memberGroups, droppedMembers, droppedSafeRefs) = BuildMembers(root, sourceId, memberAccountMap, memberGroupMap);
        accounts.AddRange(memberAccounts);

        if (droppedAccounts > 0)
        {
            logger.LogWarning("Dropped {DroppedCount} account(s) with no AccountId for source {SourceId}", droppedAccounts, sourceId);
        }
        if (droppedMembers > 0)
        {
            logger.LogWarning("Dropped {DroppedCount} member(s) with no MemberType/MemberName for source {SourceId}", droppedMembers, sourceId);
        }
        if (droppedSafes > 0)
        {
            logger.LogWarning("Dropped {DroppedCount} safe(s) with invalid or missing SafeNumber for source {SourceId}", droppedSafes, sourceId);
        }
        if (droppedSafeRefs > 0)
        {
            logger.LogWarning("Dropped {DroppedCount} safe access reference(s) with invalid or missing SafeNumber for source {SourceId}", droppedSafeRefs, sourceId);
        }

        logger.LogInformation(
            "Normalized {AccountCount} account(s), {GroupCount} group(s), {AssetCount} asset(s) for source {SourceId}",
            accounts.Count, memberGroups.Count, assets.Count, sourceId);

        return new IngestBatch(sourceId, accounts, memberGroups, assets, [], RepairedCount: export.RepairedCount, SkippedCount: 0);
    }

    private static (List<ParsedAsset> Assets, Dictionary<string, string> SafeNameToNumber, int Dropped) BuildSafes(JsonObject root, Guid sourceId, AttributeMap map)
    {
        var assets = new List<ParsedAsset>();
        var safeNameToNumber = new Dictionary<string, string>();
        var dropped = 0;

        if (root["Safes"] is not JsonArray safes)
        {
            return (assets, safeNameToNumber, dropped);
        }

        foreach (var node in safes)
        {
            if (node is not JsonObject raw)
            {
                continue;
            }

            if (MappedFieldReader.AsInt(raw["SafeNumber"]) is not int safeNumberValue)
            {
                dropped++;
                continue;
            }

            var safeNumber = safeNumberValue.ToString();
            var asset = BuildSafeAsset(raw, safeNumber, sourceId, map);
            assets.Add(asset);

            var safeName = MappedFieldReader.AsString(raw["SafeName"]);
            if (safeName is not null)
            {
                safeNameToNumber[safeName] = safeNumber;
            }
        }

        return (assets, safeNameToNumber, dropped);
    }

    private static ParsedAsset BuildSafeAsset(JsonObject raw, string safeNumber, Guid sourceId, AttributeMap map)
    {
        var safeName = MappedField(raw, map, "display_name");
        var location = MappedFieldReader.AsString(raw["Location"])?.TrimEnd('\\') ?? "";
        var fullPath = string.IsNullOrEmpty(location) ? safeName ?? "" : $"{location}\\{safeName}";

        var extra = UnmappedAttributeCollector.Collect(raw, map);
        var rawAttributes = new RawAttributesWithEdges(AliasKeys: [safeNumber], EdgeRefs: [], Extra: extra);

        var asset = new ParsedAsset(
            SourceId: sourceId,
            ConnectorType: ConnectorTypes.CyberArk,
            NativeId: safeNumber,
            AssetType: "cyberark_safe",
            DisplayName: safeName,
            FullPath: fullPath,
            FilerName: null,
            FileSize: null,
            FileCount: null,
            BrokenAcl: null,
            IsProtected: null,
            RawAttributes: rawAttributes);

        return asset with { ContentHash = ContentHasher.Hash(SafeHashableFields(asset), map.HashFields) };
    }

    internal static Dictionary<string, object?> SafeHashableFields(ParsedAsset asset) => new()
    {
        ["connector_type"] = asset.ConnectorType,
        ["native_id"] = asset.NativeId,
        ["asset_type"] = asset.AssetType,
        ["display_name"] = asset.DisplayName,
        ["full_path"] = asset.FullPath,
    };

    private static ParsedAccount BuildPrivilegedAccount(
        JsonObject raw, string nativeId, Guid sourceId, AttributeMap map, IReadOnlyDictionary<string, string> safeNameToNumber)
    {
        var extra = UnmappedAttributeCollector.Collect(raw, map);

        var edgeRefs = new List<EdgeRef>();
        var safeName = MappedFieldReader.AsString(raw["SafeName"]);
        if (safeName is not null && safeNameToNumber.TryGetValue(safeName, out var safeNumber))
        {
            edgeRefs.Add(new EdgeRef("STORED_IN", "out", safeNumber, "asset"));
        }

        var rawAttributes = new RawAttributesWithEdges(AliasKeys: [], EdgeRefs: edgeRefs, Extra: extra);

        var account = new ParsedAccount(
            SourceId: sourceId,
            ConnectorType: ConnectorTypes.CyberArk,
            NativeId: nativeId,
            AccountKind: "privileged",
            IsHuman: false,
            DisplayName: MappedField(raw, map, "display_name"),
            SamAccountName: MappedField(raw, map, "sam_account_name"),
            Upn: null,
            Email: null,
            DomainName: MappedField(raw, map, "domain_name"),
            NativeAccountId: null,
            IsDeleted: false,
            IsDisabled: false,
            RawAttributes: rawAttributes);

        return account with { ContentHash = ContentHasher.Hash(PrivilegedAccountHashableFields(account, edgeRefs), map.HashFields) };
    }

    internal static Dictionary<string, object?> PrivilegedAccountHashableFields(ParsedAccount account, IReadOnlyList<EdgeRef> edgeRefs) => new()
    {
        ["connector_type"] = account.ConnectorType,
        ["native_id"] = account.NativeId,
        ["account_kind"] = account.AccountKind,
        ["display_name"] = account.DisplayName,
        ["sam_account_name"] = account.SamAccountName,
        ["domain_name"] = account.DomainName,
        ["is_deleted"] = account.IsDeleted,
        ["is_disabled"] = account.IsDisabled,
        ["edge_refs"] = edgeRefs,
    };

    /// <summary>
    /// Consolidates <c>Members[]</c> by <c>(MemberType, MemberName)</c> before building anything —
    /// the same real member can appear once per safe they have access to, and each occurrence must
    /// contribute its own <c>HAS_ACCESS</c> edge to the *same* member entity, not one entity per row
    /// (which would lose every edge but the last-staged one to Promoter's per-manifest dedup).
    /// </summary>
    private static (List<ParsedAccount> Accounts, List<ParsedGroup> Groups, int Dropped, int DroppedSafeRefs) BuildMembers(
        JsonObject root, Guid sourceId, AttributeMap memberAccountMap, AttributeMap memberGroupMap)
    {
        var accounts = new List<ParsedAccount>();
        var groups = new List<ParsedGroup>();
        var dropped = 0;
        var droppedSafeRefs = 0;

        if (root["Members"] is not JsonArray members)
        {
            return (accounts, groups, dropped, droppedSafeRefs);
        }

        var byIdentity = new Dictionary<(string Type, string Name), List<JsonObject>>();
        foreach (var node in members)
        {
            if (node is not JsonObject raw)
            {
                continue;
            }

            var memberType = MappedFieldReader.AsString(raw["MemberType"]);
            var memberName = MappedFieldReader.AsString(raw["MemberName"]);
            if (string.IsNullOrEmpty(memberType) || string.IsNullOrEmpty(memberName))
            {
                dropped++;
                continue;
            }

            var key = (memberType, memberName);
            if (!byIdentity.TryGetValue(key, out var rows))
            {
                rows = [];
                byIdentity[key] = rows;
            }
            rows.Add(raw);
        }

        foreach (var ((memberType, memberName), rows) in byIdentity)
        {
            if (memberType == "Group")
            {
                groups.Add(BuildMemberGroup(memberName, rows, sourceId, memberGroupMap, ref droppedSafeRefs));
            }
            else
            {
                accounts.Add(BuildMemberAccount(memberName, rows, sourceId, memberAccountMap, ref droppedSafeRefs));
            }
        }

        return (accounts, groups, dropped, droppedSafeRefs);
    }

    private static ParsedAccount BuildMemberAccount(string memberName, IReadOnlyList<JsonObject> rows, Guid sourceId, AttributeMap map, ref int droppedSafeRefs)
    {
        var edgeRefs = BuildHasAccessEdgeRefs(rows, ref droppedSafeRefs);
        var extra = UnmappedAttributeCollector.Collect(rows[0], map);
        var rawAttributes = new RawAttributesWithEdges(AliasKeys: [memberName.ToLowerInvariant()], EdgeRefs: edgeRefs, Extra: extra);

        var account = new ParsedAccount(
            SourceId: sourceId,
            ConnectorType: ConnectorTypes.CyberArk,
            NativeId: memberName,
            AccountKind: "cyberark_user",
            IsHuman: true,
            DisplayName: MappedField(rows[0], map, "display_name"),
            SamAccountName: null,
            Upn: null,
            Email: null,
            DomainName: null,
            NativeAccountId: null,
            IsDeleted: false,
            IsDisabled: false,
            RawAttributes: rawAttributes);

        return account with { ContentHash = ContentHasher.Hash(MemberAccountHashableFields(account, edgeRefs), map.HashFields) };
    }

    internal static Dictionary<string, object?> MemberAccountHashableFields(ParsedAccount account, IReadOnlyList<EdgeRef> edgeRefs) => new()
    {
        ["connector_type"] = account.ConnectorType,
        ["native_id"] = account.NativeId,
        ["account_kind"] = account.AccountKind,
        ["display_name"] = account.DisplayName,
        ["is_deleted"] = account.IsDeleted,
        ["edge_refs"] = edgeRefs,
    };

    private static ParsedGroup BuildMemberGroup(string memberName, IReadOnlyList<JsonObject> rows, Guid sourceId, AttributeMap map, ref int droppedSafeRefs)
    {
        var edgeRefs = BuildHasAccessEdgeRefs(rows, ref droppedSafeRefs);
        var extra = UnmappedAttributeCollector.Collect(rows[0], map);
        var rawAttributes = new RawAttributesWithEdges(AliasKeys: [memberName.ToLowerInvariant()], EdgeRefs: edgeRefs, Extra: extra);

        var group = new ParsedGroup(
            SourceId: sourceId,
            ConnectorType: ConnectorTypes.CyberArk,
            NativeId: memberName,
            GroupClass: "cyberark_group",
            DisplayName: MappedField(rows[0], map, "display_name"),
            Email: null,
            DomainName: null,
            IsLargeGroup: false,
            IsDeleted: false,
            RawAttributes: rawAttributes);

        return group with { ContentHash = ContentHasher.Hash(MemberGroupHashableFields(group, edgeRefs), map.HashFields) };
    }

    internal static Dictionary<string, object?> MemberGroupHashableFields(ParsedGroup group, IReadOnlyList<EdgeRef> edgeRefs) => new()
    {
        ["connector_type"] = group.ConnectorType,
        ["native_id"] = group.NativeId,
        ["group_class"] = group.GroupClass,
        ["display_name"] = group.DisplayName,
        ["is_deleted"] = group.IsDeleted,
        ["edge_refs"] = edgeRefs,
    };

    private static List<EdgeRef> BuildHasAccessEdgeRefs(IReadOnlyList<JsonObject> memberRows, ref int droppedSafeRefs)
    {
        var edgeRefs = new List<EdgeRef>();
        foreach (var row in memberRows)
        {
            if (MappedFieldReader.AsInt(row["SafeNumber"]) is not int safeNumberValue)
            {
                droppedSafeRefs++;
                continue;
            }
            var safeNumber = safeNumberValue.ToString();
            edgeRefs.Add(new EdgeRef("HAS_ACCESS", "out", safeNumber, "asset", BuildPermissionsProps(row)));
        }
        return edgeRefs;
    }

    /// <summary>Clones the raw <c>Permissions</c> object verbatim (all ~20 flags) and adds a derived <c>is_admin</c> summary flag, as this edge's <c>props</c>.</summary>
    private static string BuildPermissionsProps(JsonObject member)
    {
        var permissions = member["Permissions"] as JsonObject;
        var props = permissions is not null ? (JsonObject)permissions.DeepClone() : new JsonObject();
        props["is_admin"] = MappedFieldReader.GetBool(permissions, "ManageSafe") || MappedFieldReader.GetBool(permissions, "ManageSafeMembers");
        return props.ToJsonString();
    }

    /// <summary>Looks up which raw attribute feeds <paramref name="column"/> per the connector's attribute map. CyberArk's raw values aren't LDAP array-wrapped — a straight string read.</summary>
    private static string? MappedField(JsonObject raw, AttributeMap map, string column) =>
        MappedFieldReader.Get(raw, map, column, MappedFieldReader.AsString);
}
