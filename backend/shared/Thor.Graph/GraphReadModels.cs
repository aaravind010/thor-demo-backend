namespace Thor.Graph;

/// <summary>Which way an edge points relative to the vertex a read starts from.</summary>
public enum GraphDirection
{
    Out,
    In,
    Both,
}

/// <summary>
/// A vertex as read back from Neptune. <see cref="Properties"/> holds the vertex's own properties
/// minus the <c>tenantId</c>/<c>pgId</c> bookkeeping that every vertex carries.
/// </summary>
public sealed record GraphVertexView(string EntityType, Guid Id, IReadOnlyDictionary<string, object?> Properties);

/// <summary>One edge between the start vertex and a neighbour. <see cref="Direction"/> is never <see cref="GraphDirection.Both"/>.</summary>
public sealed record GraphEdgeView(string RelType, GraphDirection Direction, IReadOnlyDictionary<string, object?> Properties);

/// <summary>A directly connected vertex and every edge linking it to the start vertex.</summary>
public sealed record GraphNeighbor(GraphVertexView Vertex, IReadOnlyList<GraphEdgeView> Edges);

/// <summary>A vertex reached over one or more hops; <see cref="Depth"/> is the shortest hop count found (1 = direct).</summary>
public sealed record GraphReachedVertex(GraphVertexView Vertex, int Depth);

/// <summary>
/// One <c>HAS_ACCESS</c> edge that grants the start vertex access to an asset. <see cref="Via"/> is
/// the group holding the edge, or null when the start vertex holds it directly.
/// </summary>
public sealed record GraphAccessGrant(GraphVertexView? Via, IReadOnlyDictionary<string, object?> Properties);

/// <summary>An asset the start vertex can reach, with every grant that leads to it.</summary>
public sealed record GraphAccess(GraphVertexView Asset, IReadOnlyList<GraphAccessGrant> Grants);

/// <summary>
/// One page of a graph read, ordered by vertex id. Pass <see cref="NextCursor"/> back as the next
/// read's <c>after</c>; it is null on the last page.
/// </summary>
public sealed record GraphPage<T>(IReadOnlyList<T> Items, Guid? NextCursor);
