namespace Thor.Rules.Ownership;

/// <summary>Legal <c>"how"</c> values for a field comparison in <c>ownership_rule.rule_definition</c>.</summary>
public static class RuleOperator
{
    public const string Exact = "exact";
    public const string Similar = "similar";
    public const string Contains = "contains";
    public const string StartsWith = "starts_with";
    public const string EndsWith = "ends_with";

    public static readonly IReadOnlyCollection<string> All = [Exact, Similar, Contains, StartsWith, EndsWith];
}
