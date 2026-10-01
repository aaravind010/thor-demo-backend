using System.Text.Json.Serialization;

namespace Thor.Rules.Ownership;

/// <summary>
/// One field comparison — the same shape used at the top level of a <c>field_match</c>/
/// <c>sibling_match</c> rule, and reused inside the rare <c>"and"</c> escape hatch for a rule that
/// needs more than one condition (e.g. Domain\SamAccountName's two-field match).
/// </summary>
public sealed class FieldCondition
{
    /// <summary>Shorthand when the entity side and identity/sibling side use the same logical field name.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("entityField")]
    public string? EntityField { get; set; }

    [JsonPropertyName("identityField")]
    public string? IdentityField { get; set; }

    [JsonPropertyName("how")]
    public string How { get; set; } = RuleOperator.Exact;

    public string? ResolvedEntityField => EntityField ?? Field;
    public string? ResolvedIdentityField => IdentityField ?? Field;
}
