using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Extraction;

/// <summary>
/// Adapts the Windows connector's raw export bytes into the clean contract normalization is
/// written against. Recovers malformed root JSON the same way <see cref="CyberArkExtractor"/>
/// does. Each <c>Filers[].LocalADPaths[]</c> entry is, like AD's <c>AccountsPaths</c>, a
/// *stringified* JSON document — those are pre-repaired and re-encoded in place here (mirroring
/// <see cref="AdExtractor"/>'s <c>AliasDistinguishedNames</c>), so <see cref="Normalization.WindowsNormalizer"/>
/// can keep parsing them with a plain, unguarded <c>JsonNode.Parse</c>.
/// </summary>
public static class WindowsExtractor
{
    public static ExtractedWindowsExport Extract(byte[] rawBytes)
    {
        var bytes = BomStripper.Strip(rawBytes);
        var (root, rootRepaired) = JsonRepair.TryParseObject(bytes);
        if (root is null)
        {
            throw new InvalidOperationException("Windows export did not parse to a JSON object.");
        }

        var repairedCount = rootRepaired ? 1 : 0;
        var skippedCount = 0;

        if (root["Filers"] is JsonArray filers)
        {
            foreach (var filerNode in filers)
            {
                if (filerNode is not JsonObject filer || filer["LocalADPaths"] is not JsonArray paths)
                {
                    continue;
                }
                RepairLocalAdPaths(paths, ref repairedCount, ref skippedCount);
            }
        }

        return new ExtractedWindowsExport(root, repairedCount, skippedCount);
    }

    /// <summary>
    /// Recovers each stringified <c>LocalADPaths[]</c> entry via <see cref="JsonRepair"/>,
    /// re-encoding a repaired entry cleanly in place; drops (and counts) anything unrecoverable.
    /// A non-stringified entry is left untouched — <see cref="Normalization.WindowsNormalizer"/>
    /// already silently skips those, unrelated to JSON malformation.
    /// </summary>
    private static void RepairLocalAdPaths(JsonArray paths, ref int repairedCount, ref int skippedCount)
    {
        for (var i = paths.Count - 1; i >= 0; i--)
        {
            if (paths[i] is not JsonValue pathValue || !pathValue.TryGetValue(out string? pathJson) || pathJson is null)
            {
                continue;
            }

            var (inner, repaired) = JsonRepair.TryParseObject(pathJson);
            if (inner is null)
            {
                paths.RemoveAt(i);
                skippedCount++;
                continue;
            }

            if (repaired)
            {
                repairedCount++;
                paths[i] = JsonValue.Create(inner.ToJsonString());
            }
        }
    }
}
