namespace Thor.Rules.Ownership;

/// <summary>
/// Allowlist of <c>(relType, direction, fromType, toType)</c> tuples a rule is permitted to hop
/// across — every real <c>RelType</c> ingestion actually produces today, plus the direction it's
/// meaningful in (<c>fromType</c>/<c>toType</c> are always the edge's own literal columns,
/// regardless of which side a rule treats as "current" vs. "target"). A hop outside this list
/// fails validation (rule skipped + logged). Adding a rule that reuses one of these tuples needs
/// no code change; recognizing a genuinely new tuple does.
/// </summary>
public static class EdgeTraversalCatalog
{
    private static readonly HashSet<(string RelType, string Direction, string FromType, string ToType)> Allowed =
    [
        ("REPORTS_TO", "out", OwnershipEntityTypes.Account, OwnershipEntityTypes.Account),
        ("MANAGED_BY", "out", OwnershipEntityTypes.Group, OwnershipEntityTypes.Account),
        ("MEMBER_OF", "out", OwnershipEntityTypes.Group, OwnershipEntityTypes.Group),
        // An account walking out to the groups it belongs to (majority-group-owner's hop).
        ("MEMBER_OF", "out", OwnershipEntityTypes.Account, OwnershipEntityTypes.Group),
        // Fan-in from a group's member accounts to that group (majority-manager's first hop).
        ("MEMBER_OF", "in", OwnershipEntityTypes.Account, OwnershipEntityTypes.Group),
        ("HAS_ACCESS", "in", OwnershipEntityTypes.Account, OwnershipEntityTypes.Asset),
        ("HAS_ACCESS", "in", OwnershipEntityTypes.Group, OwnershipEntityTypes.Asset),
        ("STORED_IN", "in", OwnershipEntityTypes.Account, OwnershipEntityTypes.Asset),
        // An account walking out to its own containing safe (CyberArk account-via-safe's first hop).
        ("STORED_IN", "out", OwnershipEntityTypes.Account, OwnershipEntityTypes.Asset),
    ];

    public static bool IsAllowed(string relType, string direction, string fromType, string toType) =>
        Allowed.Contains((relType, direction, fromType, toType));
}
