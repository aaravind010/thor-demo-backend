namespace Thor.Rules.Ownership;

/// <summary>Legal <c>ownership_rule.applies_to</c> / <c>party_assignment.entity_type</c> values.</summary>
public static class OwnershipEntityTypes
{
    public const string Account = "account";
    public const string Group = "grp";
    public const string Asset = "asset";

    public static readonly IReadOnlyCollection<string> All = [Account, Group, Asset];
}
