using Thor.Workflows.Ingestion.Models;

namespace Thor.Workflows.Ingestion.Normalization;

/// <summary>
/// The abstract, connector-agnostic normalizer contract (§3): a connector's raw, already-unzipped
/// export bytes, plus which source produced them, in — a universal <see cref="IngestBatch"/> out.
/// Each connector owns its own byte-level parsing internally (AD still runs its raw bytes through
/// <see cref="Extraction.AdExtractor"/> first, just from inside <see cref="AdNormalizer"/> now
/// rather than a pipeline step calling it directly) — there's no shared "extracted export" type
/// across connectors, since AD's/CyberArk's/Windows' raw shapes have nothing in common structurally.
/// </summary>
public interface IConnectorNormalizer
{
    IngestBatch Normalize(byte[] rawExportBytes, Guid sourceId);
}
