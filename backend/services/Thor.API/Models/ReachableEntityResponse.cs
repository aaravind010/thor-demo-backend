namespace Thor.Api.Models;

/// <summary>An entity reached over one or more hops of the same relationship.</summary>
/// <param name="Depth">Shortest number of hops found from the requested entity (1 = direct).</param>
public sealed record ReachableEntityResponse(
    RelatedEntityResponse Entity,
    int Depth);
