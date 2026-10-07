using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// A normalizer that reads its export as a stream and yields it as bounded batches, for exports too
/// large to hold whole. <paramref name="openExport"/> is called once per pass over the export.
/// </summary>
public interface IStreamingConnectorNormalizer
{
    IEnumerable<IngestBatch> NormalizeBatches(Func<Stream> openExport, Guid sourceId);
}
