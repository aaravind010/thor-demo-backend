namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// A normalized person record from an HR feed, ready for staging — same <c>ContentHash</c>
/// convention as <see cref="ParsedAccount"/>. Keyed by <c>(SourceId, HrEmployeeId)</c>. Unlike
/// accounts/groups it carries no <see cref="RawAttributesWithEdges"/>: identities neither declare
/// nor receive ingestion edges, so <c>RawAttributes</c> is just the feed fields with no column.
/// </summary>
public sealed record ParsedIdentity(
    Guid SourceId,
    string HrEmployeeId,
    string? DisplayName,
    string? Email,
    string? GivenName,
    string? Surname,
    string? Department,
    string? Title,
    string? BusinessUnit,
    string? SamAccountName,
    string? Upn,
    string? ManagerEmployeeId,
    string? ManagerName,
    string? AdMatchField,
    string? AdMatchValue,
    bool MarkedToRetire,
    object RawAttributes)
{
    public string ContentHash { get; init; } = string.Empty;
}
