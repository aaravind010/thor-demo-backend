namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// A normalized entitlement/permission-grant record, ready for staging — mirrors
/// <see cref="ParsedAccount"/>/<see cref="ParsedGroup"/>'s shape and <c>ContentHash</c>
/// convention. Entitlements don't participate in the edge graph, so <c>RawAttributes</c>
/// carries no AliasKeys/EdgeRefs contract here; it's just whatever JSON-serializable shape
/// the connector wants to preserve.
/// </summary>
public sealed record ParsedEntitlement(
    Guid SourceId,
    short ConnectorType,
    string NativeId,
    string EntitlementType,
    string? Name,
    string? Description,
    bool IsAdmin,
    string? Scope,
    string? InstanceName,
    string? SudoPath,
    string? SudoHost,
    object RawAttributes)
{
    public string ContentHash { get; init; } = string.Empty;
}
