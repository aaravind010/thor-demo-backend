namespace Thor.Api.Models;

/// <summary>An asset the requested entity can access, with every grant (direct or through a group) that leads to it.</summary>
public sealed record AccessResponse(
    RelatedEntityResponse Asset,
    IReadOnlyList<AccessGrantResponse> Grants);
