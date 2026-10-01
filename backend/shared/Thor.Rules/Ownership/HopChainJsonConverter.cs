using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thor.Rules.Ownership;

/// <summary>
/// Lets a rule's <c>"via"</c> be written as either a single hop object (one hop) or an array of
/// hop objects (a chain) — same shape either way, so a rule author never has to remember which
/// form a particular rule needs.
/// </summary>
public sealed class HopChainJsonConverter : JsonConverter<List<EdgeHop>>
{
    public override List<EdgeHop>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<List<EdgeHop>>(ref reader, options);
        }

        var single = JsonSerializer.Deserialize<EdgeHop>(ref reader, options);
        return single is null ? null : [single];
    }

    public override void Write(Utf8JsonWriter writer, List<EdgeHop> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
