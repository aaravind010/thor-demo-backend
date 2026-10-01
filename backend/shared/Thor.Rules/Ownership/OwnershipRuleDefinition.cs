using System.Text.Json;

namespace Thor.Rules.Ownership;

/// <summary>
/// Single parse + validate boundary for <c>ownership_rule.rule_definition</c> — used by the
/// Ownership workflow when loading rules and by Thor.Api when authoring them, so both accept and
/// reject exactly the same rows.
/// </summary>
public static class OwnershipRuleDefinition
{
    /// <summary>Deserializes and validates <paramref name="json"/> against the row's <paramref name="ruleType"/>/<paramref name="appliesTo"/>. Never throws for bad rule content.</summary>
    public static bool TryParse(string ruleType, string appliesTo, string json, out RuleDefinitionPayload payload, out string error)
    {
        RuleDefinitionPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RuleDefinitionPayload>(json);
        }
        catch (JsonException ex)
        {
            payload = null!;
            error = $"malformed rule_definition: {ex.Message}";
            return false;
        }

        if (parsed is null)
        {
            payload = null!;
            error = "rule_definition deserialized to null.";
            return false;
        }

        payload = parsed;
        return RuleDefinitionValidator.TryValidate(ruleType, appliesTo, parsed, out error);
    }
}
