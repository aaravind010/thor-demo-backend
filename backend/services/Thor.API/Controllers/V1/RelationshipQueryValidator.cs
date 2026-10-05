using Thor.Api.Constants;
using Thor.Graph;

namespace Thor.Api.Controllers.V1;

/// <summary>
/// Validates the query parameters shared by <see cref="AccountRelationshipsController"/> and
/// <see cref="GroupRelationshipsController"/>. Each method returns an error message for a 400, or
/// null when the value is valid.
/// </summary>
internal static class RelationshipQueryValidator
{
    public static string? TryParseDirection(string value, out GraphDirection direction)
    {
        GraphDirection? parsed = value.ToLowerInvariant() switch
        {
            "out" => GraphDirection.Out,
            "in" => GraphDirection.In,
            "both" => GraphDirection.Both,
            _ => null,
        };
        direction = parsed.GetValueOrDefault();
        return parsed is null ? GraphQueryConstants.InvalidDirectionMessage : null;
    }

    /// <summary>Upper-cases each label and rejects any the graph doesn't write, so a filter can't name an arbitrary label.</summary>
    public static string? TryParseRelTypes(string[]? values, out IReadOnlyCollection<string> relTypes)
    {
        var normalized = (values ?? []).Select(v => v.ToUpperInvariant()).Distinct().ToList();
        relTypes = normalized;
        var unknown = normalized.Where(v => !GraphRelTypes.All.Contains(v)).ToList();
        return unknown.Count == 0
            ? null
            : $"Unknown relType '{string.Join("', '", unknown)}'. Expected one of: {string.Join(", ", GraphRelTypes.All.Order())}.";
    }

    /// <summary>Direct reads are one hop; transitive reads default to <see cref="GraphQueryConstants.DefaultMaxDepth"/>.</summary>
    public static string? TryResolveDepth(bool transitive, int? maxDepth, out int depth)
    {
        depth = transitive ? maxDepth ?? GraphQueryConstants.DefaultMaxDepth : 1;
        return maxDepth is < 1 or > GraphQueryConstants.MaxDepthLimit ? GraphQueryConstants.InvalidMaxDepthMessage : null;
    }

    public static string? ValidateLimit(int limit) =>
        limit is < 1 or > PagingConstants.MaxLimit ? PagingConstants.InvalidLimitMessage : null;
}
