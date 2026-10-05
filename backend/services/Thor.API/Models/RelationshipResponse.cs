namespace Thor.Api.Models;

/// <summary>A directly connected entity and every edge between it and the requested entity.</summary>
public sealed record RelationshipResponse(
    RelatedEntityResponse Entity,
    IReadOnlyList<RelationshipEdgeResponse> Edges);
