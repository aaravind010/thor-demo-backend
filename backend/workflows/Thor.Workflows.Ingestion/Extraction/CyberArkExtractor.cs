using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Extraction;

/// <summary>
/// Adapts CyberArk's raw export bytes into the clean contract normalization is written against.
/// CyberArk's export is one clean JSON document with no BOM/multi-document quirks like AD's —
/// the only recoverable failure mode is malformed root JSON (trailing commas/comments,
/// truncation — see <see cref="JsonRepair"/>), which is repaired here rather than thrown.
/// </summary>
public static class CyberArkExtractor
{
    public static ExtractedCyberArkExport Extract(byte[] rawBytes)
    {
        var bytes = BomStripper.Strip(rawBytes);
        var (root, repaired) = JsonRepair.TryParseObject(bytes);

        return root is not null
            ? new ExtractedCyberArkExport(root, repaired ? 1 : 0)
            : throw new InvalidOperationException("CyberArk export did not parse to a JSON object.");
    }
}
