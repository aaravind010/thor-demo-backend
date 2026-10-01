using System.Text.Json.Serialization;

namespace Thor.Rules.Ownership;

/// <summary>
/// An <c>inherit_owner</c> rule's <c>"walk"</c>: repeat one hop — an edge (<see cref="RelType"/>/
/// <see cref="Direction"/>/<see cref="ToType"/>) or a self-referencing FK column
/// (<see cref="Column"/>) — until the current row has an active owner, bounded by the rule's
/// <c>maxDepth</c>.
/// </summary>
public sealed class WalkSpec
{
    [JsonPropertyName("relType")]
    public string? RelType { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("toType")]
    public string? ToType { get; set; }

    /// <summary>Self-referencing FK column mode (e.g. <c>parent_asset_id</c>) instead of an edge.</summary>
    [JsonPropertyName("column")]
    public string? Column { get; set; }
}
