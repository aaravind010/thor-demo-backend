using Thor.Api.Models;
using Thor.Graph;

namespace Thor.Api.Services;

/// <summary>Maps <see cref="IGraphRelationshipReader"/> results to the relationship endpoints' response models.</summary>
public static class RelationshipResponseMapper
{
    public static CursorPage<RelationshipResponse> ToResponse(GraphPage<GraphNeighbor> page) => new(
        page.Items.Select(n => new RelationshipResponse(
            ToEntity(n.Vertex),
            n.Edges.Select(e => new RelationshipEdgeResponse(e.RelType, ToWire(e.Direction), e.Properties)).ToList())).ToList(),
        page.NextCursor);

    public static CursorPage<ReachableEntityResponse> ToResponse(GraphPage<GraphReachedVertex> page) => new(
        page.Items.Select(r => new ReachableEntityResponse(ToEntity(r.Vertex), r.Depth)).ToList(),
        page.NextCursor);

    public static CursorPage<AccessResponse> ToResponse(GraphPage<GraphAccess> page) => new(
        page.Items.Select(a => new AccessResponse(
            ToEntity(a.Asset),
            a.Grants.Select(g => new AccessGrantResponse(g.Via is null ? null : ToEntity(g.Via), g.Properties)).ToList())).ToList(),
        page.NextCursor);

    // "grp" is the storage name (GROUP is reserved in SQL); the API says "group".
    private static RelatedEntityResponse ToEntity(GraphVertexView vertex) => new(
        vertex.EntityType == "grp" ? "group" : vertex.EntityType,
        vertex.Id,
        vertex.Properties.GetValueOrDefault("displayName") as string,
        vertex.Properties.GetValueOrDefault("nativeId") as string,
        vertex.Properties);

    private static string ToWire(GraphDirection direction) => direction == GraphDirection.Out ? "out" : "in";
}
