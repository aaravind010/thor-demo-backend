using System.Text.Json.Nodes;

namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// The output of <see cref="Extraction.CyberArkExtractor"/> and input to
/// <see cref="Normalization.CyberArkNormalizer"/>. CyberArk's export is a single clean JSON
/// document with no nested double-encoded structure, so all there is to report is whether the
/// root itself needed <see cref="Extraction.JsonRepair"/> recovery.
/// </summary>
public sealed record ExtractedCyberArkExport(JsonObject Root, int RepairedCount);
