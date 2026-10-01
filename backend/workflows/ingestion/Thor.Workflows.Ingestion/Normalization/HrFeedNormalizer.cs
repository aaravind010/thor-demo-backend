using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Extraction;
using Thor.Workflows.Ingestion.Hashing;
using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// Normalizes the HR feed connector's <c>FeedIngestorData</c> envelope (see
/// <see cref="HrFeedExtractor"/>) into <see cref="ParsedIdentity"/> records — people, not
/// accounts, so the batch carries only <see cref="IngestBatch.Identities"/>. Values are trimmed
/// (HR exports carry stray whitespace, e.g. <c>" Marketing and Sales"</c>) and email is
/// lowercased. An entry with no employee id is dropped; a repeated employee id keeps the last
/// entry, the earlier ones counted as skipped.
///
/// No UPN column mapping yet — which <c>UserSpecs</c> field carries it is unconfirmed, so it
/// lands in <c>raw_attributes</c> with the other unmapped fields until the map names it.
/// </summary>
public sealed class HrFeedNormalizer(ILogger<HrFeedNormalizer> logger, IAttributeMapProvider mapProvider) : IConnectorNormalizer
{
    private const string Connector = "HrFeed";

    public IngestBatch Normalize(byte[] rawExportBytes, Guid sourceId)
    {
        var map = mapProvider.GetMap(Connector, "Identity");
        var export = HrFeedExtractor.Extract(rawExportBytes);

        if (export.IsPostData)
        {
            logger.LogInformation("HR feed file for source {SourceId} is the raw-feed companion upload; nothing to ingest", sourceId);
            return new IngestBatch(sourceId, [], [], [], [], 0, 0);
        }

        var byEmployeeId = new Dictionary<string, ParsedIdentity>();
        var droppedCount = 0;
        var duplicateCount = 0;

        foreach (var raw in export.UserSpecs)
        {
            var hrEmployeeId = MappedField(raw, map, "hr_employee_id");
            if (hrEmployeeId is null)
            {
                droppedCount++;
                continue;
            }

            if (byEmployeeId.ContainsKey(hrEmployeeId))
            {
                duplicateCount++;
            }
            byEmployeeId[hrEmployeeId] = BuildIdentity(raw, hrEmployeeId, sourceId, map);
        }

        if (droppedCount > 0)
        {
            logger.LogWarning("Dropped {DroppedCount} HR feed record(s) with no employee id for source {SourceId}", droppedCount, sourceId);
        }
        if (duplicateCount > 0)
        {
            logger.LogWarning("Dropped {DuplicateCount} HR feed record(s) repeating an earlier employee id for source {SourceId}; the last one wins", duplicateCount, sourceId);
        }

        logger.LogInformation("Normalized {IdentityCount} identity(s) for source {SourceId}", byEmployeeId.Count, sourceId);

        return new IngestBatch(sourceId, [], [], [], [], 0, export.SkippedCount + droppedCount + duplicateCount)
        {
            Identities = byEmployeeId.Values.ToList(),
        };
    }

    private static ParsedIdentity BuildIdentity(JsonObject raw, string hrEmployeeId, Guid sourceId, AttributeMap map)
    {
        var identity = new ParsedIdentity(
            SourceId: sourceId,
            HrEmployeeId: hrEmployeeId,
            DisplayName: MappedField(raw, map, "display_name"),
            Email: MappedField(raw, map, "email")?.ToLowerInvariant(),
            GivenName: MappedField(raw, map, "given_name"),
            Surname: MappedField(raw, map, "surname"),
            Department: MappedField(raw, map, "department"),
            Title: MappedField(raw, map, "title"),
            BusinessUnit: MappedField(raw, map, "business_unit"),
            SamAccountName: MappedField(raw, map, "sam_account_name"),
            Upn: MappedField(raw, map, "upn"),
            ManagerEmployeeId: MappedField(raw, map, "manager_employee_id"),
            ManagerName: MappedField(raw, map, "manager_name"),
            AdMatchField: MappedField(raw, map, "ad_match_field"),
            AdMatchValue: MappedField(raw, map, "ad_match_value"),
            MarkedToRetire: map.ColumnMappings.TryGetValue("marked_to_retire", out var retireAttribute) && MappedFieldReader.GetBool(raw, retireAttribute),
            RawAttributes: UnmappedAttributeCollector.Collect(raw, map));

        return identity with { ContentHash = ContentHasher.Hash(HashableFields(identity), map.HashFields) };
    }

    internal static Dictionary<string, object?> HashableFields(ParsedIdentity identity) => new()
    {
        ["hr_employee_id"] = identity.HrEmployeeId,
        ["display_name"] = identity.DisplayName,
        ["email"] = identity.Email,
        ["given_name"] = identity.GivenName,
        ["surname"] = identity.Surname,
        ["department"] = identity.Department,
        ["title"] = identity.Title,
        ["business_unit"] = identity.BusinessUnit,
        ["sam_account_name"] = identity.SamAccountName,
        ["upn"] = identity.Upn,
        ["manager_employee_id"] = identity.ManagerEmployeeId,
        ["manager_name"] = identity.ManagerName,
        ["ad_match_field"] = identity.AdMatchField,
        ["ad_match_value"] = identity.AdMatchValue,
        ["marked_to_retire"] = identity.MarkedToRetire,
    };

    /// <summary>Trimmed mapped value, with blank treated as absent. HR feed values are plain strings, like CyberArk's/Windows'.</summary>
    private static string? MappedField(JsonObject raw, AttributeMap map, string column) =>
        MappedFieldReader.Get(raw, map, column, MappedFieldReader.AsString)?.Trim() is { Length: > 0 } value ? value : null;
}
