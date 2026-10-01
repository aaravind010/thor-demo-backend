namespace Thor.Rules.Ownership;

/// <summary>
/// The SQL-injection boundary for entity-side field references: a fixed, hand-written allowlist
/// mapping a rule's logical field name to a safe SQL expression template (<c>{alias}</c> is
/// substituted with whatever table alias the compiler assigned this row/hop-target). A field name
/// not present here fails validation before any SQL is built — rule JSON text is never concatenated
/// into a query.
/// </summary>
public static class EntityFieldCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Account = new Dictionary<string, string>
    {
        ["email"] = "lower({alias}.email)",
        ["upn_local_part"] = "lower(split_part(coalesce({alias}.upn, ''), '@', 1))",
        ["display_name"] = "lower({alias}.display_name)",
        ["sam_account_name"] = "lower({alias}.sam_account_name)",
        ["domain_name"] = "lower({alias}.domain_name)",
        // FIXED: real serialized key is "Department" (capital D) — Stager.cs serializes
        // RawAttributes with default PascalCase JsonSerializer output, not lowercase.
        ["department"] = "lower({alias}.raw_attributes::jsonb ->> 'Department')",
    };

    private static readonly IReadOnlyDictionary<string, string> Grp = new Dictionary<string, string>
    {
        ["email"] = "lower({alias}.email)",
        ["display_name"] = "lower({alias}.display_name)",
    };

    private static readonly IReadOnlyDictionary<string, string> Asset = new Dictionary<string, string>
    {
        ["display_name"] = "lower({alias}.display_name)",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByEntityType =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [OwnershipEntityTypes.Account] = Account,
            [OwnershipEntityTypes.Group] = Grp,
            [OwnershipEntityTypes.Asset] = Asset,
        };

    /// <summary>Resolves a logical field name to a safe SQL expression for the given entity type and alias, or null if unrecognized.</summary>
    public static string? Resolve(string entityType, string field, string alias) =>
        ByEntityType.TryGetValue(entityType, out var fields) && fields.TryGetValue(field, out var template)
            ? template.Replace("{alias}", alias)
            : null;
}
