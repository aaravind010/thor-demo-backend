using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.Extraction;
using Thor.Workflows.Ingestion.Hashing;
using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// Normalizes the Windows (local accounts) connector's raw export (one clean JSON document) into
/// accounts and groups. Each <c>Filers[]</c> entry's <c>LocalADPaths[]</c> is, like AD's
/// <c>AccountsPaths</c>, an array of *stringified* JSON holding an <c>Accounts</c> array, split
/// into users/groups by <c>ObjectClass</c>. Groups' <c>DirectMembers[]</c> (member stubs keyed by
/// <c>SamAccountName</c>, not a DN) become deferred <see cref="EdgeRef"/>s resolved the same way
/// AD's DN-keyed refs are — <see cref="RawAttributesWithEdges.AliasKeys"/> matching is scoped by
/// <c>(ConnectorType, SourceId)</c>, so this connector's own alias namespace never collides with
/// AD's even though these keys are shorter and more collision-prone.
///
/// <c>WindowsServicePaths</c>/<c>SharesPaths</c>/etc. within each Filer are read but not
/// normalized — out of scope (no asset/service entity concept populated by this connector yet).
/// </summary>
public sealed class WindowsNormalizer(ILogger<WindowsNormalizer> logger, IAttributeMapProvider mapProvider) : IConnectorNormalizer
{
    public IngestBatch Normalize(byte[] rawExportBytes, Guid sourceId)
    {
        var accountMap = mapProvider.GetMap(ConnectorTypes.Windows, "Account");
        var groupMap = mapProvider.GetMap(ConnectorTypes.Windows, "Group");

        var export = WindowsExtractor.Extract(rawExportBytes);
        var root = export.Root;

        var accounts = new List<ParsedAccount>();
        var groups = new List<ParsedGroup>();
        var droppedCount = 0;

        if (root["Filers"] is JsonArray filers)
        {
            foreach (var filerNode in filers)
            {
                if (filerNode is not JsonObject filer || filer["LocalADPaths"] is not JsonArray localAdPaths)
                {
                    continue;
                }

                foreach (var pathNode in localAdPaths)
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

                        var nativeId = (MappedFieldReader.AsString(raw["ObjectGuid"]) ?? MappedFieldReader.AsString(raw["Sid"]))?.ToLowerInvariant();
                        if (string.IsNullOrEmpty(nativeId))
                        {
                            droppedCount++;
                            continue;
                        }

                        if (MappedFieldReader.AsString(raw["ObjectClass"]) == "Group")
                        {
                            groups.Add(BuildGroup(raw, nativeId, sourceId, groupMap));
                        }
                        else
                        {
                            accounts.Add(BuildAccount(raw, nativeId, sourceId, accountMap));
                        }
                    }
                }
            }
        }

        if (droppedCount > 0)
        {
            logger.LogWarning("Dropped {DroppedCount} record(s) with no ObjectGuid/Sid for source {SourceId}", droppedCount, sourceId);
        }

        logger.LogInformation(
            "Normalized {AccountCount} account(s) and {GroupCount} group(s) for source {SourceId}",
            accounts.Count, groups.Count, sourceId);

        return new IngestBatch(sourceId, accounts, groups, [], [], export.RepairedCount, export.SkippedCount);
    }

    private static ParsedAccount BuildAccount(JsonObject raw, string nativeId, Guid sourceId, AttributeMap map)
    {
        var samAccountName = MappedFieldReader.AsString(raw["SamAccountName"]);
        var extra = UnmappedAttributeCollector.Collect(raw, map);
        var rawAttributes = new RawAttributesWithEdges(
            AliasKeys: samAccountName is not null ? [samAccountName.ToLowerInvariant()] : [],
            EdgeRefs: [],
            Extra: extra);

        var account = new ParsedAccount(
            SourceId: sourceId,
            ConnectorType: ConnectorTypes.Windows,
            NativeId: nativeId,
            AccountKind: "user",
            IsHuman: true,
            DisplayName: MappedField(raw, map, "display_name"),
            SamAccountName: samAccountName,
            Upn: null,
            Email: MappedField(raw, map, "email"),
            DomainName: MappedField(raw, map, "domain_name"),
            NativeAccountId: MappedField(raw, map, "native_account_id"),
            IsDeleted: false,
            IsDisabled: !MappedFieldReader.GetBool(raw, "AccountStatus"),
            RawAttributes: rawAttributes);

        return account with { ContentHash = ContentHasher.Hash(HashableFields(account), map.HashFields) };
    }

    internal static Dictionary<string, object?> HashableFields(ParsedAccount account) => new()
    {
        ["connector_type"] = account.ConnectorType,
        ["native_id"] = account.NativeId,
        ["account_kind"] = account.AccountKind,
        ["display_name"] = account.DisplayName,
        ["sam_account_name"] = account.SamAccountName,
        ["email"] = account.Email,
        ["domain_name"] = account.DomainName,
        ["is_deleted"] = account.IsDeleted,
        ["is_disabled"] = account.IsDisabled,
    };

    private static ParsedGroup BuildGroup(JsonObject raw, string nativeId, Guid sourceId, AttributeMap map)
    {
        var edgeRefs = new List<EdgeRef>();
        if (raw["DirectMembers"] is JsonArray directMembers)
        {
            foreach (var memberNode in directMembers)
            {
                var memberSam = memberNode is JsonObject member ? MappedFieldReader.AsString(member["SamAccountName"]) : null;
                if (!string.IsNullOrEmpty(memberSam))
                {
                    edgeRefs.Add(new EdgeRef("MEMBER_OF", "in", memberSam.ToLowerInvariant(), "account"));
                }
            }
        }

        var extra = UnmappedAttributeCollector.Collect(raw, map);
        var rawAttributes = new RawAttributesWithEdges(
            AliasKeys: [nativeId],
            EdgeRefs: edgeRefs,
            Extra: extra);

        var group = new ParsedGroup(
            SourceId: sourceId,
            ConnectorType: ConnectorTypes.Windows,
            NativeId: nativeId,
            GroupClass: "local_security",
            DisplayName: MappedField(raw, map, "display_name"),
            Email: MappedField(raw, map, "email"),
            DomainName: MappedField(raw, map, "domain_name"),
            IsLargeGroup: false,
            IsDeleted: false,
            RawAttributes: rawAttributes);

        return group with { ContentHash = ContentHasher.Hash(HashableFields(group, edgeRefs), map.HashFields) };
    }

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

    /// <summary>Looks up which raw attribute feeds <paramref name="column"/> per the connector's attribute map. Windows' raw values, like CyberArk's, aren't LDAP array-wrapped.</summary>
    private static string? MappedField(JsonObject raw, AttributeMap map, string column) =>
        MappedFieldReader.Get(raw, map, column, MappedFieldReader.AsString);
}
