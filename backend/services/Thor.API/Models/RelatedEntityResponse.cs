namespace Thor.Api.Models;

/// <summary>An entity at the other end of a graph relationship.</summary>
/// <param name="EntityType">One of <c>account</c>, <c>group</c>, <c>asset</c>, <c>identity</c>.</param>
/// <param name="Properties">Every property the graph holds for the entity, including <see cref="DisplayName"/>/<see cref="NativeId"/>.</param>
public sealed record RelatedEntityResponse(
    string EntityType,
    Guid Id,
    string? DisplayName,
    string? NativeId,
    IReadOnlyDictionary<string, object?> Properties);
