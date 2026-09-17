namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// A normalized group record (§5.2.3), ready for staging. <c>ContentHash</c> defaults to
/// empty and must be set last, via <see cref="Hashing.ContentHasher.Hash"/>, once every
/// other field is finalized — per §3.4/§5.4.
///
/// <c>RawAttributes</c> is deliberately untyped — see <see cref="ParsedAccount"/>'s remarks.
/// </summary>
public sealed record ParsedGroup(
    Guid SourceId,
    short ConnectorType,
    string NativeId,
    string GroupClass,
    string? DisplayName,
    string? Email,
    string? DomainName,
    bool IsLargeGroup,
    bool IsDeleted,
    object RawAttributes)
{
    public string ContentHash { get; init; } = string.Empty;
}
