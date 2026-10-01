using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thor.Rules.AccountType;

/// <summary>
/// Deserialized shape of an <c>AccountTypeRule.RuleDefinition</c>
/// JSON tree: either a leaf <c>{field, operator, value}</c> or a compound <c>{"and": [...]}</c> /
/// <c>{"or": [...]}</c>. <see cref="Value"/> is left as a <see cref="JsonElement"/> — automatically
/// cloned by <see cref="JsonSerializer"/> when deserialized into a POCO property, so it stays valid
/// independent of the original parse buffer — since a rule's value can be a JSON string, boolean,
/// or number.
/// </summary>
public sealed class RuleNode
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }

    [JsonPropertyName("and")]
    public List<RuleNode>? And { get; set; }

    [JsonPropertyName("or")]
    public List<RuleNode>? Or { get; set; }
}
