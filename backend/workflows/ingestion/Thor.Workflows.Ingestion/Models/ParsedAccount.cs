namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// A normalized account record (§5.2.2), ready for staging. <c>ContentHash</c> defaults to
/// empty and must be set last, via <see cref="Hashing.ContentHasher.Hash"/>, once every
/// other field is finalized — per §3.4/§5.4.
///
/// <c>RawAttributes</c> is deliberately untyped (<see cref="object"/>): every connector uses
/// <see cref="RawAttributesWithEdges"/> — the only contract it must satisfy is
/// "JSON-serializable", enforced by <see cref="Staging.Stager"/>
/// at the point it's written to the <c>raw_attributes</c> column. Fields a normalizer needs
/// typed access to (e.g. <c>EdgeRefs</c> for content hashing) should be captured from a local
/// variable before boxing into this property, not read back off it.
/// </summary>
public sealed record ParsedAccount(
    Guid SourceId,
    short ConnectorType,
    string NativeId,
    string AccountKind,
    bool IsHuman,
    string? DisplayName,
    string? SamAccountName,
    string? Upn,
    string? Email,
    string? DomainName,
    string? NativeAccountId,
    bool IsDeleted,
    bool IsDisabled,
    object RawAttributes)
{
    public string ContentHash { get; init; } = string.Empty;
}
