using System.Text.Json.Serialization;

namespace Thor.Rules.Ownership;

/// <summary>
/// Deserialized shape of an <c>ownership_rule.rule_definition</c> JSON value — pure parameters,
/// no discriminator inside it (that lives in the row's own <c>RuleType</c> column). Every field
/// below is relevant to only some <c>RuleType</c>s; <c>OwnershipRuleCompiler</c> reads only
/// the members relevant to the row's own type. Deliberately flat: the common case ("this field
/// should match that field") is a two-or-three-key object, not a nested tree.
/// </summary>
public sealed class RuleDefinitionPayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    // --- field_match / sibling_match primary condition ---

    /// <summary>Shorthand when the entity side and identity/sibling side use the same logical field name.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("entityField")]
    public string? EntityField { get; set; }

    [JsonPropertyName("identityField")]
    public string? IdentityField { get; set; }

    [JsonPropertyName("how")]
    public string How { get; set; } = RuleOperator.Exact;

    /// <summary>Rare escape hatch: additional AND-ed conditions on the same entity/identity pair (e.g. Domain\SamAccountName's two fields).</summary>
    [JsonPropertyName("and")]
    public List<FieldCondition>? And { get; set; }

    /// <summary><c>field_match</c> only: cap on candidates per entity (generalizes the old hardcoded department cap).</summary>
    [JsonPropertyName("cap")]
    public int? Cap { get; set; }

    public string? ResolvedEntityField => EntityField ?? Field;
    public string? ResolvedIdentityField => IdentityField ?? Field;

    // --- hop chain: field_match (optional prefix hop), inherit_owner (via), majority_owner (via) ---

    [JsonPropertyName("via")]
    [JsonConverter(typeof(HopChainJsonConverter))]
    public List<EdgeHop>? Via { get; set; }

    // --- inherit_owner only ---

    [JsonPropertyName("walk")]
    public WalkSpec? Walk { get; set; }

    [JsonPropertyName("maxDepth")]
    public int? MaxDepth { get; set; }

    [JsonPropertyName("prefer")]
    public PreferSpec? Prefer { get; set; }
}
