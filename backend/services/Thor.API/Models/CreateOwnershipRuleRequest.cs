namespace Thor.Api.Models;

/// <param name="RuleDefinition">The rule parameters as a JSON string, e.g. <c>{"schemaVersion":1,"field":"email","how":"exact"}</c>.</param>
public sealed record CreateOwnershipRuleRequest(
    string RuleName,
    string RuleType,
    string AppliesTo,
    string RuleDefinition,
    decimal PrecisionScore,
    bool IsActive = true);
