namespace Thor.Api.Exceptions;

/// <summary>An ownership rule already uses this name, or the name is reserved for a built-in default rule.</summary>
public sealed class OwnershipRuleNameConflictException(string ruleName)
    : Exception($"Ownership rule name '{ruleName}' is already in use or reserved.")
{
    public string RuleName { get; } = ruleName;
}
