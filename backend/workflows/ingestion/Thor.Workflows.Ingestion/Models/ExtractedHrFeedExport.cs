using System.Text.Json.Nodes;

namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// The output of <see cref="Extraction.HrFeedExtractor"/> and input to
/// <see cref="Normalization.HrFeedNormalizer"/>.
/// </summary>
/// <param name="UserSpecs">The envelope's <c>UserSpecs[]</c> entries — one per person, still raw.</param>
/// <param name="IsPostData">The file is the connector's <c>FeedIngestorPostData</c> companion (raw CSV
/// text only, no people) rather than a <c>FeedIngestorData</c> envelope — there is nothing to ingest.</param>
/// <param name="SkippedCount"><c>UserSpecs[]</c> entries that weren't JSON objects and were dropped.</param>
public sealed record ExtractedHrFeedExport(IReadOnlyList<JsonObject> UserSpecs, bool IsPostData, int SkippedCount);
