namespace Thor.Api.Constants;

/// <summary>Bounds for the multi-hop graph endpoints under <c>/accounts/{id}</c> and <c>/groups/{id}</c>.</summary>
public static class GraphQueryConstants
{
    /// <summary>Nesting levels followed when a transitive read gives no <c>maxDepth</c>.</summary>
    public const int DefaultMaxDepth = 5;

    /// <summary>Hard cap: each extra level multiplies the paths a traversal walks.</summary>
    public const int MaxDepthLimit = 10;

    public const string InvalidMaxDepthMessage = "maxDepth must be between 1 and 10.";

    public const string InvalidDirectionMessage = "direction must be 'out', 'in' or 'both'.";
}
