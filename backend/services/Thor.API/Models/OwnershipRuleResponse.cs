namespace Thor.Api.Models;

public sealed record OwnershipRuleResponse(
    Guid Id,
    string RuleName,
    string RuleType,
    string RuleDefinition,
    string AppliesTo,
    bool IsActive,
    int TotalPredictions,
    int TruePositiveCount,
    int FalsePositiveCount,
    decimal PrecisionScore,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
