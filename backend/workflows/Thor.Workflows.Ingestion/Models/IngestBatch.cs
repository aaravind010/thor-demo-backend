namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// The universal normalizer output (§3.2): every entity type a connector produced,
/// grouped together. AD produces accounts and groups only; CyberArk produces accounts (privileged
/// credentials and CyberArk-scoped Members), groups (Member groups), and assets (Safes) — no
/// entitlements, now that Member permission grants are edges, not entitlements; Windows produces
/// accounts and groups (no assets, no entitlements).
///
/// <c>RepairedCount</c>/<c>SkippedCount</c> are diagnostics for malformed/unrecoverable raw
/// records a connector's own extraction recovered or dropped (see <c>AdExtractor</c>/
/// <c>JsonRepair</c>) — they live here, not on a connector-specific "extracted export" type,
/// so <see cref="Normalization.IConnectorNormalizer"/> has one uniform diagnostics surface
/// regardless of which connector produced the batch. Connectors with no repair logic of their
/// own (CyberArk, Windows) just report zero for both.
/// </summary>
public sealed record IngestBatch(
    Guid SourceId,
    IReadOnlyList<ParsedAccount> Accounts,
    IReadOnlyList<ParsedGroup> Groups,
    IReadOnlyList<ParsedAsset> Assets,
    IReadOnlyList<ParsedEntitlement> Entitlements,
    int RepairedCount,
    int SkippedCount);
