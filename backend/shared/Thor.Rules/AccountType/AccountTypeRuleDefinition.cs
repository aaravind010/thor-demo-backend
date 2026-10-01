using System.Text.Json;

namespace Thor.Rules.AccountType;

/// <summary>
/// Single parse + validate boundary for <c>account_type_rule.rule_definition</c> — used by the ATRE
/// workflow when loading rules and by Thor.Api when authoring them, so both accept and reject
/// exactly the same rows.
/// </summary>
public static class AccountTypeRuleDefinition
{
    /// <summary>The only <c>account_type_rule.applies_to</c> value ATRE evaluates.</summary>
    public const string AccountAppliesTo = "account";

    /// <summary>Deserializes and validates <paramref name="json"/> as a <see cref="RuleNode"/> tree. Never throws for bad rule content.</summary>
    public static bool TryParse(string json, out RuleNode node, out string error)
    {
        RuleNode? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RuleNode>(json);
        }
        catch (JsonException ex)
        {
            node = null!;
            error = $"malformed rule_definition: {ex.Message}";
            return false;
        }

        if (parsed is null)
        {
            node = null!;
            error = "rule_definition deserialized to null.";
            return false;
        }

        node = parsed;
        return RuleNodeValidator.IsValid(parsed, out error);
    }
}
