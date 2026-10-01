namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// A normalized governed-resource record (a safe, share, folder, etc.), ready for staging —
/// mirrors <see cref="ParsedAccount"/>/<see cref="ParsedGroup"/>'s shape and <c>ContentHash</c>
/// convention. Unlike accounts/groups, an asset is typically only ever an edge *target*, never an
/// owner declaring its own outbound refs — but it still needs an <c>AliasKeys</c> entry (via
/// <see cref="RawAttributesWithEdges"/>) so other entities' edges can resolve against it.
/// </summary>
public sealed record ParsedAsset(
    Guid SourceId,
    short ConnectorType,
    string NativeId,
    string AssetType,
    string? DisplayName,
    string? FullPath,
    string? FilerName,
    long? FileSize,
    long? FileCount,
    bool? BrokenAcl,
    bool? IsProtected,
    object RawAttributes)
{
    public string ContentHash { get; init; } = string.Empty;
}
