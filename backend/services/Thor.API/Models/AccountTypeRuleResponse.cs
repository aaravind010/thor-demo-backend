namespace Thor.Api.Models;

public sealed record AccountTypeRuleResponse(
    Guid Id,
    string RuleName,
    string RuleDefinition,
    string AppliesTo,
    Guid TargetAccountTypeId,
    bool IsActive,
    int TotalPredictions,
    int TruePositiveCount,
    int FalsePositiveCount,
    decimal PrecisionScore,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
