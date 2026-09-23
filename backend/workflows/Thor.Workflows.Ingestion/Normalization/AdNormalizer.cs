using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.Extraction;
using Thor.Workflows.Ingestion.Hashing;
using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// Normalizes AD's clean export contract (§5.1) into canonical account/group records
/// (§5.2, §5.3). The Accounts array in each AccountsPath mixes users, computers, and
/// groups; entities are split by <c>objectClass</c>. Edges are never resolved here — every
/// entity only declares its deferred <c>alias_keys</c>/<c>edge_refs</c> (§3.3), left for the
/// (deferred) edge gate.
///
/// Which raw attribute feeds each of the "straight rename" columns (e.g. <c>display_name</c>,
/// <c>email</c>) is driven by <see cref="IAttributeMapProvider"/> rather than hardcoded here —
/// see <c>AttributeMapping/Config/ad-account.json</c>/<c>ad-group.json</c>. Fields that need
/// real transformation logic (account_kind, domain_name, is_disabled, group_class) stay
/// hardcoded below, since a simple attribute rename can't express them.
/// </summary>
public sealed class AdNormalizer(ILogger<AdNormalizer> logger, IAttributeMapProvider mapProvider) : IConnectorNormalizer
{
    private static readonly Dictionary<int, string> GroupTypeMap = new()
    {
        [-2147483646] = "security_global",
        [-2147483644] = "security_domainlocal",
        [-2147483640] = "security_universal",
        [2] = "distribution_global",
        [4] = "distribution_domainlocal",
        [8] = "distribution_universal",
    };

    public IngestBatch Normalize(byte[] rawExportBytes, Guid sourceId)
    {
        var export = AdExtractor.Extract(rawExportBytes);
        var accountMap = mapProvider.GetMap(ConnectorTypes.ActiveDirectory, "Account");
        var groupMap = mapProvider.GetMap(ConnectorTypes.ActiveDirectory, "Group");
        var accounts = new List<ParsedAccount>();
        var groups = new List<ParsedGroup>();
        var droppedCount = 0;

        foreach (var domainNode in export.Domains)
        {
            if (domainNode is not JsonObject domain || domain["AccountsPaths"] is not JsonArray accountsPaths)
            {
                continue;
            }

            foreach (var pathNode in accountsPaths)
            {
                if (pathNode is not JsonValue pathValue || !pathValue.TryGetValue(out string? pathJson) || pathJson is null)
                {
                    continue;
                }

                if (JsonNode.Parse(pathJson) is not JsonObject inner || inner["Accounts"] is not JsonArray rawAccounts)
                {
                    continue;
                }

                foreach (var rawNode in rawAccounts)
                {
                    if (rawNode is not JsonObject raw)
                    {
                        continue;
                    }

                    // Records with no objectGUID are dropped; count logged once after the loop
                    // rather than per record, to avoid flooding logs on a bad export.
                    var nativeId = First(raw["objectGUID"])?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(nativeId))
                    {
                        droppedCount++;
                        continue;
                    }

                    var objectClass = GetStringList(raw, "objectClass");
                    if (objectClass.Contains("group"))
                    {
                        groups.Add(BuildGroup(raw, nativeId, sourceId, groupMap));
                    }
                    else
                    {
                        accounts.Add(BuildAccount(raw, objectClass, nativeId, sourceId, accountMap));
                    }
                }
            }
        }

        if (droppedCount > 0)
        {
            logger.LogWarning("Dropped {DroppedCount} record(s) with no objectGUID for source {SourceId}", droppedCount, sourceId);
        }

        logger.LogInformation(
            "Normalized {AccountCount} account(s) and {GroupCount} group(s) for source {SourceId}",
            accounts.Count, groups.Count, sourceId);

        return new IngestBatch(sourceId, accounts, groups, [], [], export.RepairedCount, export.SkippedCount);
    }

    private static ParsedAccount BuildAccount(JsonObject raw, IReadOnlyList<string> objectClass, string nativeId, Guid sourceId, AttributeMap accountMap)
    {
        var isComputer = objectClass.Contains("computer");
        var accountKind = isComputer ? "computer" : "user";
        var isHuman = accountKind == "user";

        var dn = First(raw["dn"]);

        var edgeRefs = new List<EdgeRef>();
        foreach (var memberOf in GetStringList(raw, "memberOf"))
        {
            if (!string.IsNullOrEmpty(memberOf))
            {
                edgeRefs.Add(new EdgeRef("MEMBER_OF", "out", memberOf.ToLowerInvariant(), "grp"));
            }
        }
        var manager = First(raw["manager"]);
        if (!string.IsNullOrEmpty(manager))
        {
            edgeRefs.Add(new EdgeRef("REPORTS_TO", "out", manager.ToLowerInvariant(), "account"));
        }

        var extra = UnmappedAttributeCollector.Collect(raw, accountMap);
        var rawAttributes = new RawAttributesWithEdges(
            AliasKeys: dn is not null ? [dn.ToLowerInvariant()] : [],
            EdgeRefs: edgeRefs,
            Extra: extra);

        var account = new ParsedAccount(
            SourceId: sourceId,
            ConnectorType: ConnectorTypes.ActiveDirectory,
            NativeId: nativeId,
            AccountKind: accountKind,
            IsHuman: isHuman,
            DisplayName: MappedField(raw, accountMap, "display_name") ?? First(raw["name"]),
            SamAccountName: MappedField(raw, accountMap, "sam_account_name"),
            Upn: MappedField(raw, accountMap, "upn"),
            Email: MappedField(raw, accountMap, "email"),
            DomainName: dn is not null ? SplitFirstThenReplace(dn, ",DC=", ".") : null,
            NativeAccountId: MappedField(raw, accountMap, "native_account_id"),
            IsDeleted: false,
            IsDisabled: IsDisabled(raw["userAccountControl"]),
            RawAttributes: rawAttributes);

        return account with { ContentHash = ContentHasher.Hash(HashableFields(account, edgeRefs), accountMap.HashFields) };
    }

    private static ParsedGroup BuildGroup(JsonObject raw, string nativeId, Guid sourceId, AttributeMap groupMap)
    {
        var isDistributionList = raw["IsDL"]?.GetValue<bool>() ?? false;
        var groupType = First(raw["groupType"]);
        var groupClass = DecodeGroupType(groupType, isDistributionList);
        var dn = First(raw["dn"]) ?? First(raw["distinguishedName"]);

        var edgeRefs = new List<EdgeRef>();
        foreach (var member in GetStringList(raw, "member"))
        {
            if (!string.IsNullOrEmpty(member))
            {
                edgeRefs.Add(new EdgeRef("MEMBER_OF", "in", member.ToLowerInvariant()));
            }
        }
        foreach (var memberOf in GetStringList(raw, "memberOf"))
        {
            if (!string.IsNullOrEmpty(memberOf))
            {
                edgeRefs.Add(new EdgeRef("MEMBER_OF", "out", memberOf.ToLowerInvariant()));
            }
        }
        var managedBy = First(raw["managedBy"]);
        if (!string.IsNullOrEmpty(managedBy))
        {
            edgeRefs.Add(new EdgeRef("MANAGED_BY", "out", managedBy.ToLowerInvariant()));
        }

        var extra = UnmappedAttributeCollector.Collect(raw, groupMap);
        var rawAttributes = new RawAttributesWithEdges(
            AliasKeys: dn is not null ? [dn.ToLowerInvariant()] : [],
            EdgeRefs: edgeRefs,
            Extra: extra);

        var group = new ParsedGroup(
            SourceId: sourceId,
            ConnectorType: ConnectorTypes.ActiveDirectory,
            NativeId: nativeId,
            GroupClass: groupClass,
            DisplayName: MappedField(raw, groupMap, "display_name") ?? First(raw["cn"]),
            Email: MappedField(raw, groupMap, "email"),
            DomainName: DeriveGroupDomainName(dn),
            IsLargeGroup: raw["IsLargeGroup"]?.GetValue<bool>() ?? false,
            IsDeleted: false,
            RawAttributes: rawAttributes);

        return group with { ContentHash = ContentHasher.Hash(HashableFields(group, edgeRefs), groupMap.HashFields) };
    }

    /// <summary>Looks up which raw attribute feeds <paramref name="column"/> per the connector's attribute map, then applies AD's single-valued-attribute unwrapping convention (see <see cref="First"/>). A column absent from the map is not sourced from raw data at all.</summary>
    private static string? MappedField(JsonObject raw, AttributeMap map, string column) =>
        MappedFieldReader.Get(raw, map, column, First);

    internal static Dictionary<string, object?> HashableFields(ParsedAccount account, IReadOnlyList<EdgeRef> edgeRefs) => new()
    {
        ["connector_type"] = account.ConnectorType,
        ["native_id"] = account.NativeId,
        ["account_kind"] = account.AccountKind,
        ["display_name"] = account.DisplayName,
        ["upn"] = account.Upn,
        ["email"] = account.Email,
        ["domain_name"] = account.DomainName,
        ["is_deleted"] = account.IsDeleted,
        ["is_disabled"] = account.IsDisabled,
        ["edge_refs"] = edgeRefs,
    };

    internal static Dictionary<string, object?> HashableFields(ParsedGroup group, IReadOnlyList<EdgeRef> edgeRefs) => new()
    {
        ["connector_type"] = group.ConnectorType,
        ["native_id"] = group.NativeId,
        ["group_class"] = group.GroupClass,
        ["display_name"] = group.DisplayName,
        ["email"] = group.Email,
        ["domain_name"] = group.DomainName,
        ["is_deleted"] = group.IsDeleted,
        ["edge_refs"] = edgeRefs,
    };

    private static string DecodeGroupType(string? groupTypeRaw, bool isDistributionList)
    {
        if (isDistributionList)
        {
            return "distribution";
        }
        return int.TryParse(groupTypeRaw, out var groupType) && GroupTypeMap.TryGetValue(groupType, out var decoded)
            ? decoded
            : "security_global";
    }

    private static string? DeriveGroupDomainName(string? dn)
    {
        if (string.IsNullOrEmpty(dn))
        {
            return null;
        }

        var dcComponents = dn.Split(',')
            .Where(part => part.Trim().StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(part => part.Split('=', 2)[1])
            .ToList();

        return dcComponents.Count > 0 ? string.Join(".", dcComponents).ToLowerInvariant() : null;
    }

    /// <summary>
    /// Account domain_name derivation (§5.2.2): tail of the DN after its first ",DC=",
    /// with every remaining ",DC=" replaced by ".". Deliberately a different algorithm
    /// from <see cref="DeriveGroupDomainName"/> — both preserved independently, per spec.
    /// </summary>
    private static string SplitFirstThenReplace(string s, string separator, string replacement)
    {
        var index = s.IndexOf(separator, StringComparison.Ordinal);
        var tail = index >= 0 ? s[(index + separator.Length)..] : s;
        return tail.Replace(separator, replacement);
    }

    private static bool IsDisabled(JsonNode? userAccountControl)
    {
        var raw = First(userAccountControl);
        return int.TryParse(string.IsNullOrEmpty(raw) ? "0" : raw, out var value) && (value & 2) != 0;
    }

    /// <summary>
    /// LDAP convention: single-valued attributes arrive wrapped in a one-element array; an
    /// empty string is treated as absent. Mirrors the POC's <c>_first(lst, default=None)</c>.
    /// </summary>
    private static string? First(JsonNode? node)
    {
        if (node is JsonArray array && array.Count > 0)
        {
            var text = array[0]?.GetValue<string>();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        return null;
    }

    private static List<string> GetStringList(JsonObject obj, string key)
    {
        if (obj[key] is JsonArray array)
        {
            return array.Select(n => n?.GetValue<string>() ?? "").ToList();
        }
        return [];
    }
}
