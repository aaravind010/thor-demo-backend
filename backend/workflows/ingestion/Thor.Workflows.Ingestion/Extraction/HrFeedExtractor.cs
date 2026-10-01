using System.Text.Json;
using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Extraction;

/// <summary>
/// Reads the HR feed connector's upload — the connector has already parsed the tenant's HR CSV
/// into a <c>FeedIngestorData</c> JSON envelope whose <c>UserSpecs[]</c> holds one entry per
/// person. The connector also uploads a <c>FeedIngestorPostData</c> companion for the same run
/// (the raw CSV text as <c>RawFeedData</c>); that is recognized and reported as
/// <see cref="ExtractedHrFeedExport.IsPostData"/> rather than failed.
///
/// Unlike the other extractors, malformed JSON is deliberately *not* recovered through
/// <see cref="JsonRepair"/>: an HR upload is a full snapshot, and promotion deactivates everyone
/// missing from it — a truncated document salvaged into a partial roster would wrongly deactivate
/// the people cut off. A document that doesn't parse, or isn't a recognized envelope, throws, so
/// the file is marked failed and stages nothing.
/// </summary>
public static class HrFeedExtractor
{
    public static ExtractedHrFeedExport Extract(byte[] rawBytes)
    {
        var bytes = BomStripper.Strip(rawBytes);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("HR feed export is not valid JSON.", ex);
        }

        if (root is not JsonObject envelope)
        {
            throw new InvalidOperationException("HR feed export did not parse to a JSON object.");
        }

        switch (envelope["UserSpecs"])
        {
            case JsonArray userSpecs:
                var specs = userSpecs.OfType<JsonObject>().ToList();
                return new ExtractedHrFeedExport(specs, IsPostData: false, SkippedCount: userSpecs.Count - specs.Count);
            case null when envelope.ContainsKey("RawFeedData"):
                return new ExtractedHrFeedExport([], IsPostData: true, SkippedCount: 0);
            default:
                throw new InvalidOperationException("HR feed export has neither a UserSpecs array nor RawFeedData.");
        }
    }
}
