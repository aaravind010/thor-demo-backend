namespace Thor.Api.Models;

/// <param name="RelType">Edge label, e.g. <c>MEMBER_OF</c>, <c>HAS_ACCESS</c>.</param>
/// <param name="Direction"><c>out</c> when the requested entity is the edge's source, <c>in</c> when it is the target.</param>
/// <param name="Properties">The edge's own properties (e.g. the permission set on <c>HAS_ACCESS</c>); empty for most labels.</param>
public sealed record RelationshipEdgeResponse(
    string RelType,
    string Direction,
    IReadOnlyDictionary<string, object?> Properties);
