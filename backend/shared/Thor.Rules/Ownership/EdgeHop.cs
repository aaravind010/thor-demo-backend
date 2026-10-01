using System.Text.Json.Serialization;

namespace Thor.Rules.Ownership;

/// <summary>
/// One <c>tenant.edge</c> hop declared in a rule's JSON: which <c>rel_type</c>, and which
/// direction relative to whatever row the hop chain is currently standing on ("out": the edge
/// goes FROM current TO the target; "in": the edge comes FROM the target INTO current).
/// <see cref="ToType"/> is required for "out"; <see cref="FromType"/>/<see cref="FromTypes"/> is
/// required for "in" (a list when the fan-in can come from more than one entity type, e.g.
/// <c>HAS_ACCESS</c> from either an account or a group).
/// </summary>
public sealed class EdgeHop
{
    [JsonPropertyName("relType")]
    public string RelType { get; set; } = "";

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "";

    [JsonPropertyName("fromType")]
    public string? FromType { get; set; }

    [JsonPropertyName("fromTypes")]
    public List<string>? FromTypes { get; set; }

    [JsonPropertyName("toType")]
    public string? ToType { get; set; }

    /// <summary>All legal source types on the "target" side when <see cref="Direction"/> is <c>"in"</c> (falls back to a single <see cref="FromType"/>).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ResolvedFromTypes => FromTypes is { Count: > 0 } list ? list : (FromType is { } single ? [single] : []);
}
