using System.Text.Json;
using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Extraction;

/// <summary>
/// Adapts AD's raw, real-world export bytes into the clean contract normalization is written
/// against. Handles several quirks: several top-level JSON documents concatenated with a
/// leading UTF-8 BOM; accounts that carry only <c>distinguishedName</c> (never <c>dn</c>), which
/// account normalization otherwise can't read; malformed JSON that's recoverable (trailing
/// commas/comments, truncation — see <see cref="JsonRepair"/>); and known alternate shapes
/// (a <c>Domains</c>/<c>AccountsPaths</c> wrapper omitted for a single item, or an AccountsPaths
/// entry given as a nested object instead of double-encoded JSON). Anything genuinely
/// unrecognizable or unrecoverable is dropped and counted rather than thrown. Zip handling is
/// out of scope — this takes already-unzipped bytes.
/// </summary>
public static class AdExtractor
{
    public static ExtractedAdExport Extract(byte[] rawBytes)
    {
        var bytes = BomStripper.Strip(rawBytes);
        var merged = new JsonArray();
        var repairedCount = 0;
        var skippedCount = 0;

        foreach (var document in ReadTopLevelDocuments(bytes, ref repairedCount, ref skippedCount))
        {
            var domains = ExtractDomainList(document, ref skippedCount);
            if (domains is null)
            {
                continue;
            }

            while (domains.Count > 0)
            {
                var domainNode = domains[0];
                domains.RemoveAt(0);

                if (domainNode is JsonObject domainObj)
                {
                    AliasDistinguishedNames(domainObj, ref repairedCount, ref skippedCount);
                }

                merged.Add(domainNode);
            }
        }

        return new ExtractedAdExport(merged, ConnectorTypes.ActiveDirectory, repairedCount, skippedCount);
    }

    /// <summary>
    /// Recognizes the expected <c>{"Domains":[...]}</c> shape plus two exporter variants: a
    /// single domain given directly as <c>{"Domains":{...}}</c> (array wrapper omitted), and the
    /// <c>Domains</c> wrapper itself omitted entirely (the document *is* the one domain).
    /// </summary>
    private static JsonArray? ExtractDomainList(JsonNode? document, ref int skippedCount)
    {
        if (document is not JsonObject obj)
        {
            skippedCount++;
            return null;
        }

        switch (obj["Domains"])
        {
            case JsonArray domains:
                return domains;
            case JsonObject singleDomain:
                obj.Remove("Domains");
                return new JsonArray(singleDomain);
            case null when obj["AccountsPaths"] is not null:
                return new JsonArray(obj);
            default:
                skippedCount++;
                return null;
        }
    }

    /// <summary>
    /// Splits several concatenated top-level JSON values with no separator between them —
    /// mirrors Python's <c>json.JSONDecoder().raw_decode</c> loop. A single <see cref="Utf8JsonReader"/>
    /// can't be reused across values (it errors with "expected end of data" once a complete
    /// top-level value has been consumed and non-whitespace bytes remain), so each value gets
    /// its own reader scoped to the unconsumed remainder, advancing by <c>BytesConsumed</c>. A
    /// document that fails to parse is handed to <see cref="JsonRepair"/>: if recovered, it's
    /// counted as repaired and included; otherwise it's counted as skipped and dropped, and
    /// splitting resumes just past it.
    /// </summary>
    private static List<JsonNode?> ReadTopLevelDocuments(byte[] bytes, ref int repairedCount, ref int skippedCount)
    {
        var documents = new List<JsonNode?>();
        var offset = 0;

        while (offset < bytes.Length)
        {
            while (offset < bytes.Length && IsJsonWhitespace(bytes[offset]))
            {
                offset++;
            }
            if (offset >= bytes.Length)
            {
                break;
            }

            var reader = new Utf8JsonReader(bytes.AsSpan(offset));
            try
            {
                reader.Read();
                documents.Add(JsonNode.Parse(ref reader));
                offset += (int)reader.BytesConsumed;
                continue;
            }
            catch (JsonException)
            {
                // fall through to recovery below
            }

            var (node, bytesConsumed) = JsonRepair.TryParse(bytes.AsSpan(offset));
            if (node is not null)
            {
                documents.Add(node);
                repairedCount++;
            }
            else
            {
                skippedCount++;
            }
            offset += bytesConsumed;
        }

        return documents;
    }

    private static bool IsJsonWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    /// <summary>
    /// Normalizes one domain's AccountsPaths entries into the clean, always-double-encoded-JSON
    /// shape <see cref="Normalization.AdNormalizer"/> expects, aliasing <c>distinguishedName</c>
    /// onto <c>dn</c> along the way. Recognizes an entry already given as a nested object
    /// (instead of double-encoded JSON) and re-encodes it; recovers a malformed JSON string via
    /// <see cref="JsonRepair"/>; drops (and counts) anything else.
    /// </summary>
    private static void AliasDistinguishedNames(JsonObject domain, ref int repairedCount, ref int skippedCount)
    {
        JsonArray accountsPaths;
        switch (domain["AccountsPaths"])
        {
            case JsonArray array:
                accountsPaths = array;
                break;
            case JsonObject singlePath:
                domain.Remove("AccountsPaths");
                accountsPaths = new JsonArray(singlePath);
                domain["AccountsPaths"] = accountsPaths;
                break;
            default:
                return;
        }

        for (var i = accountsPaths.Count - 1; i >= 0; i--)
        {
            JsonObject? inner;
            switch (accountsPaths[i])
            {
                case JsonObject alreadyDecoded:
                    inner = alreadyDecoded;
                    break;
                case JsonValue pathValue when pathValue.TryGetValue(out string? pathJson) && pathJson is not null:
                    var (parsed, repaired) = JsonRepair.TryParseObject(pathJson);
                    inner = parsed;
                    if (repaired)
                    {
                        repairedCount++;
                    }
                    break;
                default:
                    inner = null;
                    break;
            }

            if (inner is null)
            {
                accountsPaths.RemoveAt(i);
                skippedCount++;
                continue;
            }

            if (inner["Accounts"] is JsonArray accounts)
            {
                foreach (var accountNode in accounts)
                {
                    if (accountNode is JsonObject account
                        && !account.ContainsKey("dn")
                        && account.TryGetPropertyValue("distinguishedName", out var dn)
                        && dn is not null)
                    {
                        account["dn"] = dn.DeepClone();
                    }
                }
            }

            accountsPaths[i] = JsonValue.Create(inner.ToJsonString());
        }
    }
}
