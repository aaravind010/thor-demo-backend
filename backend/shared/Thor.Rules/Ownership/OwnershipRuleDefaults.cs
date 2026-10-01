namespace Thor.Rules.Ownership;

/// <summary>One default rule row — plain data, not logic. Used only to populate a brand-new tenant's <c>ownership_rule</c> table; every field here is exactly what an operator could later edit directly in the DB.</summary>
public sealed record OwnershipRuleSeed(string RuleName, string RuleType, string AppliesTo, string RuleDefinitionJson, decimal PrecisionScore, bool IsActive);

/// <summary>
/// The built-in rules, seeded once per tenant by <c>OwnershipRuleSeeder</c>. This is the
/// only place their content is authored — from here on, changing what a rule does, its weight, or
/// whether it's on is a DB <c>UPDATE</c>, never a redeploy. Weights below are reasonable starting
/// points, not calibrated precision — <c>ownership_rule.precision_score</c> exists precisely so an
/// operator can retune them from real true/false-positive data over time.
/// </summary>
public static class OwnershipRuleDefaults
{
    public const string EmailExactMatch = "Email exact match to Identity";
    public const string ManagerAttribute = "AD manager attribute";
    public const string UpnCorrelation = "UPN-to-email correlation";
    public const string DisplayNameMatch = "Display name match";
    public const string DepartmentFallback = "Same department fallback";
    public const string GroupManagedByInheritance = "AD group managed-by inheritance";
    public const string GroupNestingInheritance = "Group nesting parent-owner inheritance";
    public const string AssetParentInheritance = "Parent asset inheritance";
    public const string AssetCyberArkAccessInference = "CyberArk privileged-access inference";
    public const string EmployeeIdInAccountName = "Employee ID in account name";
    public const string EmployeeIdInDisplayName = "Employee ID in display name";
    public const string CyberArkAccountViaSafe = "CyberArk account owner via safe access";
    public const string SameAccountName = "Same account name";
    public const string SimilarAccountName = "Similar account name";
    public const string MajorityManager = "Majority manager";
    public const string MajorityGroupOwner = "Majority group owner";

    public const int DepartmentFallbackCap = 5;

    public static readonly IReadOnlyList<OwnershipRuleSeed> All =
    [
        new(EmailExactMatch, OwnershipRuleType.FieldMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"field":"email","how":"exact"}""",
            0.90m, true),

        // FIXED: legacy rule read raw_attributes->>'manager_email', which no normalizer ever
        // writes — now hops the real REPORTS_TO edge to the manager's own account row.
        new(ManagerAttribute, OwnershipRuleType.FieldMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"via":{"relType":"REPORTS_TO","direction":"out","toType":"account"},"field":"email","how":"exact"}""",
            0.70m, true),

        new(UpnCorrelation, OwnershipRuleType.FieldMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"entityField":"upn_local_part","identityField":"email_local_part","how":"exact"}""",
            0.65m, true),

        new(DisplayNameMatch, OwnershipRuleType.FieldMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"field":"display_name","how":"exact"}""",
            0.55m, true),

        // FIXED: legacy rule read raw_attributes->>'department' (lowercase); the real serialized
        // key is "Department" (capital D) — corrected in EntityFieldCatalog, not here.
        new(DepartmentFallback, OwnershipRuleType.FieldMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"field":"department","how":"exact","cap":__CAP__}""".Replace("__CAP__", DepartmentFallbackCap.ToString()),
            0.40m, true),

        new(GroupManagedByInheritance, OwnershipRuleType.InheritOwner, OwnershipEntityTypes.Group,
            """{"schemaVersion":1,"via":{"relType":"MANAGED_BY","direction":"out","toType":"account"}}""",
            0.80m, true),

        new(GroupNestingInheritance, OwnershipRuleType.InheritOwner, OwnershipEntityTypes.Group,
            """{"schemaVersion":1,"walk":{"relType":"MEMBER_OF","direction":"out","toType":"grp"},"maxDepth":10}""",
            0.75m, true),

        new(AssetParentInheritance, OwnershipRuleType.InheritOwner, OwnershipEntityTypes.Asset,
            """{"schemaVersion":1,"walk":{"column":"parent_asset_id"},"maxDepth":20}""",
            0.75m, true),

        // Seeded INACTIVE: mere access (especially non-admin) is a weak ownership signal, and the
        // required Edge(ToId, RelType) index may not exist in every environment yet. An operator
        // flips this on per tenant — after reviewing precision on real data — with a plain
        // `UPDATE tenant.ownership_rule SET is_active = true WHERE rule_name = '...'`.
        new(AssetCyberArkAccessInference, OwnershipRuleType.InheritOwner, OwnershipEntityTypes.Asset,
            """{"schemaVersion":1,"via":{"relType":"HAS_ACCESS","direction":"in","fromTypes":["account","grp"]},"prefer":{"prop":"is_admin","value":true}}""",
            0.60m, false),

        new(EmployeeIdInAccountName, OwnershipRuleType.FieldMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"entityField":"sam_account_name","identityField":"employee_id","how":"contains"}""",
            0.50m, true),

        new(EmployeeIdInDisplayName, OwnershipRuleType.FieldMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"entityField":"display_name","identityField":"employee_id","how":"contains"}""",
            0.45m, true),

        // Seeded INACTIVE for the same reason as AssetCyberArkAccessInference — the second hop is
        // the same weak, ToId-index-dependent HAS_ACCESS traversal.
        new(CyberArkAccountViaSafe, OwnershipRuleType.FieldMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"via":[{"relType":"STORED_IN","direction":"out","toType":"asset"},{"relType":"HAS_ACCESS","direction":"in","fromTypes":["account","grp"]}],"field":"email","how":"exact"}""",
            0.55m, false),

        new(SameAccountName, OwnershipRuleType.SiblingMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"field":"display_name","how":"exact"}""",
            0.50m, true),

        new(SimilarAccountName, OwnershipRuleType.SiblingMatch, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"field":"display_name","how":"similar"}""",
            0.35m, true),

        new(MajorityManager, OwnershipRuleType.MajorityOwner, OwnershipEntityTypes.Group,
            """{"schemaVersion":1,"via":[{"relType":"MEMBER_OF","direction":"in","fromType":"account"},{"relType":"REPORTS_TO","direction":"out","toType":"account"}]}""",
            0.65m, true),

        // Depends on grp party_assignment rows, which won't exist yet on a tenant's very first
        // run (grp's own rules haven't produced anything to inherit from) — converges over
        // subsequent runs as groups accumulate owners. See the phase-order design note.
        new(MajorityGroupOwner, OwnershipRuleType.MajorityOwner, OwnershipEntityTypes.Account,
            """{"schemaVersion":1,"via":{"relType":"MEMBER_OF","direction":"out","toType":"grp"}}""",
            0.55m, true),
    ];
}
