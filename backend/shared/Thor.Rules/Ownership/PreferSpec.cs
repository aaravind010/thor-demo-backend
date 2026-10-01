using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thor.Rules.Ownership;

/// <summary>Soft tie-break for <c>inherit_owner</c>: among several reachable candidates, prefer (not require) an edge whose <see cref="Prop"/> equals <see cref="Value"/>.</summary>
public sealed class PreferSpec
{
    [JsonPropertyName("prop")]
    public string Prop { get; set; } = "";

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}
