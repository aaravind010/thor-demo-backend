namespace Thor.Api.Models;

/// <summary>One <c>HAS_ACCESS</c> edge leading to an asset.</summary>
/// <param name="Via">The group holding the grant, or null when the requested entity holds it directly.</param>
/// <param name="Permissions">The grant's permission set, as stored on the edge.</param>
public sealed record AccessGrantResponse(
    RelatedEntityResponse? Via,
    IReadOnlyDictionary<string, object?> Permissions);
