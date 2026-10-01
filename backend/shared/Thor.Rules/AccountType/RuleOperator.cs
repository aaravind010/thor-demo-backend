namespace Thor.Rules.AccountType;

/// <summary>Operator names supported in an <c>account_type_rule.rule_definition</c> leaf node.</summary>
public static class RuleOperator
{
    public const string Equal = "equals";
    public const string NotEquals = "not_equals";
    public const string Contains = "contains";
    public const string StartsWith = "starts_with";
    public const string EndsWith = "ends_with";
    public const string IsNull = "is_null";
    public const string IsNotNull = "is_not_null";
    public const string Regex = "regex";

    /// <summary>Every operator string a leaf node may legally use.</summary>
    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Equal, NotEquals, Contains, StartsWith, EndsWith, IsNull, IsNotNull, Regex,
    };
}
