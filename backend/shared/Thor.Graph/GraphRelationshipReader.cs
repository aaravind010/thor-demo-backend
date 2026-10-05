using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Remote;
using Gremlin.Net.Process.Traversal;
using Gremlin.Net.Structure;
using static Gremlin.Net.Process.Traversal.AnonymousTraversalSource;

namespace Thor.Graph;

/// <summary>
/// Submits traversals as bytecode, not script strings: Neptune parses Gremlin with its own grammar
/// and does not support script bindings, so bytecode is the only way to keep every caller-derived
/// value (ids, cursors, labels) a typed argument rather than text spliced into a query.
///
/// Pages are ordered by the neighbour's <c>pgId</c> string. Ordering needs the whole candidate set,
/// so each page walks the start vertex's full neighbourhood — cheap for an account, proportional
/// to membership for a large group.
/// </summary>
public sealed class GraphRelationshipReader : IGraphRelationshipReader, IDisposable
{
    private const string TenantIdKey = "tenantId";
    private const string PgIdKey = "pgId";

    private readonly Lazy<GraphTraversalSource> _g;
    private readonly IDisposable? _ownedConnection;

    public GraphRelationshipReader(NeptuneOptions options)
    {
        // Lazy so constructing the singleton never opens a connection; the first read does.
        var factory = new GremlinConnectionFactory(options);
        _g = new Lazy<GraphTraversalSource>(() => Traversal().With(new DriverRemoteConnection(factory.Client, "g")));
        _ownedConnection = factory;
    }

    /// <summary>Test-only seam — lets tests substitute a fake or local <see cref="IGremlinClient"/> instead of a real Neptune connection.</summary>
    internal GraphRelationshipReader(IGremlinClient client)
    {
        _g = new Lazy<GraphTraversalSource>(() => Traversal().With(new DriverRemoteConnection(client, "g")));
        _ownedConnection = null;
    }

    public async Task<GraphPage<GraphNeighbor>> GetNeighborsAsync(
        Guid tenantId, GraphVertexRef start, GraphDirection direction, IReadOnlyCollection<string> relTypes,
        Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        var tid = tenantId.ToString();
        var labels = relTypes.ToArray();

        var root = StartVertex(tenantId, start);
        var neighbours = direction switch
        {
            GraphDirection.Out => root.OutE(labels).InV(),
            GraphDirection.In => root.InE(labels).OutV(),
            _ => root.Union<Vertex>(__.OutE(labels).InV(), __.InE(labels).OutV()),
        };

        var vertexRows = await ProjectVertexPage(neighbours.Has(TenantIdKey, tid), after, limit)
            .Promise(t => t.ToList(), cancellationToken);
        var vertices = vertexRows.Select(row => ParseVertexRow(row!)).ToList();
        var (pageVertices, nextCursor) = TrimToPage(vertices, limit, v => v.View.Id);
        if (pageVertices.Count == 0)
        {
            return new GraphPage<GraphNeighbor>([], null);
        }

        // Second round trip for the edges, restricted to this page's neighbours, rather than
        // projecting them per neighbour: from a large group's side that would walk every member edge
        // once per row.
        var pageIds = pageVertices.Select(v => (object)v.Vid).ToArray();
        var edgeBranches = new List<ITraversal>();
        if (direction is GraphDirection.Out or GraphDirection.Both)
        {
            edgeBranches.Add(ProjectEdge(__.OutE(labels).Where(__.InV().HasId(P.Within(pageIds))), __.InV(), "out"));
        }
        if (direction is GraphDirection.In or GraphDirection.Both)
        {
            edgeBranches.Add(ProjectEdge(__.InE(labels).Where(__.OutV().HasId(P.Within(pageIds))), __.OutV(), "in"));
        }

        var edgeRows = await StartVertex(tenantId, start)
            .Union<IDictionary<string, object>>(edgeBranches.ToArray())
            .Promise(t => t.ToList(), cancellationToken);

        var edgesByNeighbour = edgeRows
            .OfType<IDictionary<string, object>>()
            .Select(row => (Other: Convert.ToString(row["other"])!, Edge: new GraphEdgeView(
                Convert.ToString(row["relType"])!,
                Convert.ToString(row["direction"]) == "out" ? GraphDirection.Out : GraphDirection.In,
                GraphResultParser.WithoutBookkeeping(GraphResultParser.ToDictionary(row["props"])))))
            .ToLookup(x => x.Other, x => x.Edge);

        var items = pageVertices
            .Select(v => new GraphNeighbor(v.View, edgesByNeighbour[v.Vid].ToList()))
            .ToList();
        return new GraphPage<GraphNeighbor>(items, nextCursor);
    }

    public async Task<GraphPage<GraphReachedVertex>> GetReachableAsync(
        Guid tenantId, GraphVertexRef start, string relType, GraphDirection direction, int maxDepth,
        Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        var tid = tenantId.ToString();
        var hop = direction switch
        {
            GraphDirection.Out => __.Out(relType),
            GraphDirection.In => __.In(relType),
            _ => __.Both(relType),
        };

        // Group by vertex keeping the shortest path length, rather than dedup() on the first arrival:
        // repeat() gives no ordering guarantee, so the first traverser to reach a vertex needn't be
        // the shallowest. path() counts the start vertex too, so depth = length - 1.
        var entries = StartVertex(tenantId, start)
            .Repeat(hop.Has(TenantIdKey, tid).SimplePath()).Emit().Times(maxDepth)
            .Project<object>("v", "len").By(__.Identity()).By(__.Path().Count(Scope.Local))
            .Group<object, object>().By(__.Select<object>("v")).By(__.Select<object>("len").Min<object>())
            .Unfold<object>();

        if (after is { } cursor)
        {
            entries = entries.Where(__.Select<object>(Column.Keys).Has(PgIdKey, P.Gt(cursor.ToString())));
        }

        var rows = await entries
            .Order().By(__.Select<object>(Column.Keys).Values<object>(PgIdKey))
            .Limit<object>(limit + 1)
            .Project<object>("vid", "label", "props", "len")
            .By(__.Select<object>(Column.Keys).Id())
            .By(__.Select<object>(Column.Keys).Label())
            .By(__.Select<object>(Column.Keys).ValueMap<object, object>().By(__.Unfold<object>()))
            .By(__.Select<object>(Column.Values))
            .Promise(t => t.ToList(), cancellationToken);

        var reached = rows
            .OfType<IDictionary<string, object>>()
            .Select(row => new GraphReachedVertex(ParseVertexRow(row).View, Convert.ToInt32(row["len"]) - 1))
            .ToList();
        var (items, nextCursor) = TrimToPage(reached, limit, r => r.Vertex.Id);
        return new GraphPage<GraphReachedVertex>(items, nextCursor);
    }

    public async Task<GraphPage<GraphAccess>> GetEffectiveAccessAsync(
        Guid tenantId, GraphVertexRef start, int maxDepth, Guid? after, int limit, CancellationToken cancellationToken = default)
    {
        var tid = tenantId.ToString();

        // The start vertex's own HAS_ACCESS edges, plus those of every group it reaches over
        // MEMBER_OF. A fresh traversal per use: Gremlin.Net traversals are mutable builders.
        GraphTraversal<Vertex, Edge> Grants() => StartVertex(tenantId, start)
            .Union<Edge>(
                __.OutE(GraphRelTypes.HasAccess),
                __.Repeat(__.Out(GraphRelTypes.MemberOf).Has(TenantIdKey, tid).SimplePath()).Emit().Times(maxDepth)
                    .OutE(GraphRelTypes.HasAccess))
            .Dedup();

        var assetRows = await ProjectVertexPage(Grants().InV().Has(TenantIdKey, tid), after, limit)
            .Promise(t => t.ToList(), cancellationToken);
        var assets = assetRows.Select(row => ParseVertexRow(row!)).ToList();
        var (pageAssets, nextCursor) = TrimToPage(assets, limit, a => a.View.Id);
        if (pageAssets.Count == 0)
        {
            return new GraphPage<GraphAccess>([], null);
        }

        var startVid = GraphIds.VertexId(tenantId, start.EntityType, start.Id);
        var pageIds = pageAssets.Select(a => (object)a.Vid).ToArray();
        var grantRows = await Grants()
            .Where(__.InV().HasId(P.Within(pageIds)))
            .Project<object>("asset", "viaVid", "viaLabel", "viaProps", "props")
            .By(__.InV().Id())
            .By(__.OutV().Id())
            .By(__.OutV().Label())
            .By(__.OutV().ValueMap<object, object>().By(__.Unfold<object>()))
            .By(__.ValueMap<object, object>())
            .Promise(t => t.ToList(), cancellationToken);

        var grantsByAsset = grantRows
            .OfType<IDictionary<string, object>>()
            .Select(row =>
            {
                var viaVid = Convert.ToString(row["viaVid"]);
                var via = viaVid == startVid
                    ? null
                    : GraphResultParser.ToVertexView(Convert.ToString(row["viaLabel"])!, row["viaProps"]);
                var properties = GraphResultParser.WithoutBookkeeping(GraphResultParser.ToDictionary(row["props"]));
                return (Asset: Convert.ToString(row["asset"])!, Grant: new GraphAccessGrant(via, properties));
            })
            .ToLookup(x => x.Asset, x => x.Grant);

        var items = pageAssets
            .Select(a => new GraphAccess(a.View, grantsByAsset[a.Vid].ToList()))
            .ToList();
        return new GraphPage<GraphAccess>(items, nextCursor);
    }

    // The tenant filter on the start vertex is redundant with the tenant-namespaced id; it stays
    // so a vertex written with a mismatched tenantId property still can't be read.
    private GraphTraversal<Vertex, Vertex> StartVertex(Guid tenantId, GraphVertexRef start) =>
        _g.Value.V(GraphIds.VertexId(tenantId, start.EntityType, start.Id)).Has(TenantIdKey, tenantId.ToString());

    // limit + 1 rows: the extra one signals another page without a count (same as KeysetPaging).
    private static GraphTraversal<Vertex, IDictionary<string, object>> ProjectVertexPage(
        GraphTraversal<Vertex, Vertex> vertices, Guid? after, int limit)
    {
        if (after is { } cursor)
        {
            vertices = vertices.Has(PgIdKey, P.Gt(cursor.ToString()));
        }

        return vertices
            .Dedup()
            .Order().By(PgIdKey)
            .Limit<Vertex>(limit + 1)
            .Project<object>("vid", "label", "props")
            .By(T.Id)
            .By(T.Label)
            .By(__.ValueMap<object, object>().By(__.Unfold<object>()));
    }

    private static GraphTraversal<object, IDictionary<string, object>> ProjectEdge(
        GraphTraversal<object, Edge> edges, GraphTraversal<object, Vertex> otherEnd, string direction) =>
        edges
            .Project<object>("other", "relType", "direction", "props")
            .By(otherEnd.Id())
            .By(T.Label)
            .By(__.Constant<object>(direction))
            .By(__.ValueMap<object, object>());

    private static (string Vid, GraphVertexView View) ParseVertexRow(IDictionary<string, object> row) =>
        (Convert.ToString(row["vid"])!, GraphResultParser.ToVertexView(Convert.ToString(row["label"])!, row["props"]));

    private static (List<T> Items, Guid? NextCursor) TrimToPage<T>(List<T> rows, int limit, Func<T, Guid> key)
    {
        if (rows.Count <= limit)
        {
            return (rows, null);
        }

        rows.RemoveAt(limit);
        return (rows, key(rows[^1]));
    }

    public void Dispose() => _ownedConnection?.Dispose();
}
