namespace Thor.Rules.Ownership;

/// <summary>Same purpose as <see cref="EntityFieldCatalog"/>, for the candidate <c>tenant.identity</c> side — entity-type-independent.</summary>
public static class IdentityFieldCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>
    {
        ["email"] = "lower({alias}.email)",
        ["email_local_part"] = "lower(split_part(coalesce({alias}.email, ''), '@', 1))",
        ["display_name"] = "lower({alias}.display_name)",
        ["department"] = "lower({alias}.department)",
        ["employee_id"] = "lower({alias}.hr_employee_id)",
    };

    /// <summary>Resolves a logical field name to a safe SQL expression for the given alias, or null if unrecognized.</summary>
    public static string? Resolve(string field, string alias) =>
        Fields.TryGetValue(field, out var template) ? template.Replace("{alias}", alias) : null;
}
