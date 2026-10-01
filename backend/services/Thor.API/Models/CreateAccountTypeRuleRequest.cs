namespace Thor.Api.Models;

/// <param name="RuleDefinition">The rule tree as a JSON string, e.g. <c>{"field":"account_kind","operator":"equals","value":"service"}</c>.</param>
public sealed record CreateAccountTypeRuleRequest(
    string RuleName,
    string RuleDefinition,
    string AppliesTo,
    Guid TargetAccountTypeId,
    decimal PrecisionScore,
    bool IsActive = true);
