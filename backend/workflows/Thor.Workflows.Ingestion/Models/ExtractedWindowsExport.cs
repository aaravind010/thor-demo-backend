using System.Text.Json.Nodes;

namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// The output of <see cref="Extraction.WindowsExtractor"/> and input to
/// <see cref="Normalization.WindowsNormalizer"/>.
/// </summary>
/// <param name="RepairedCount">The root document, plus any <c>LocalADPaths[]</c> entries, that
/// were malformed but successfully recovered.</param>
/// <param name="SkippedCount"><c>LocalADPaths[]</c> entries that were unrecoverable and dropped.</param>
public sealed record ExtractedWindowsExport(JsonObject Root, int RepairedCount, int SkippedCount);
