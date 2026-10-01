using Thor.DataLayer.Models.Tenants;

namespace Thor.Workflows.Atre.Tests.Fixtures;

internal static class AtreSeedData
{
    public static readonly Guid HumanId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ServiceAccountId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid PrivilegedServiceAccountId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid ComputerId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid OrphanId = Guid.Parse("10000000-0000-0000-0000-000000000005");

    public static List<AccountType> BuildAccountTypes() =>
    [
        new() { Id = WellKnownAccountTypes.Unclassified, Name = "Unclassified", Description = "Default account type for newly-promoted accounts pending classification.", IsHuman = false },
        new() { Id = HumanId, Name = "Human", Description = "Regular human employee account", IsHuman = true },
        new() { Id = ServiceAccountId, Name = "Service Account", Description = "Non-human service or automation account", IsHuman = false },
        new() { Id = PrivilegedServiceAccountId, Name = "Privileged Service Account", Description = "Admin or elevated service account", IsHuman = false },
        new() { Id = ComputerId, Name = "Computer", Description = "Machine or workstation account", IsHuman = false },
        new() { Id = OrphanId, Name = "Orphan", Description = "Account with no identifiable owner or purpose", IsHuman = false },
    ];

    public static List<AccountTypeRule> BuildRules()
    {
        var now = DateTimeOffset.UtcNow;

        AccountTypeRule Rule(string name, string field, string op, string value, Guid targetTypeId, decimal weight) => new()
        {
            Id = Guid.NewGuid(),
            RuleName = name,
            RuleDefinition = $$"""{"field":"{{field}}","operator":"{{op}}","value":"{{value}}"}""",
            AppliesTo = "account",
            TargetAccountTypeId = targetTypeId,
            IsActive = true,
            PrecisionScore = weight,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return
        [
            Rule("AD user is Human", "account_kind", "equals", "user", HumanId, 0.55m),
            Rule("ServiceAccounts OU", "dn", "contains", "ou=serviceaccounts", ServiceAccountId, 0.85m),
            Rule("Tier 0 admin OU", "dn", "contains", "ou=tier 0", PrivilegedServiceAccountId, 0.90m),
            Rule("Tier 0 leaf OU (T0-)", "dn", "contains", "ou=t0-", PrivilegedServiceAccountId, 0.90m),
            Rule("objectClass computer", "account_kind", "equals", "computer", ComputerId, 0.99m),
            Rule("Service principal kind", "account_kind", "equals", "service_principal", ServiceAccountId, 0.92m),
            Rule("App registration kind", "account_kind", "equals", "app_registration", ServiceAccountId, 0.95m),
            Rule("SQL login is service", "account_kind", "equals", "sql_login", ServiceAccountId, 0.80m),
        ];
    }

    public static Account BuildAccount(Guid sourceId, string nativeId, string accountKind, string rawAttributesJson = "{}")
    {
        var now = DateTimeOffset.UtcNow;
        return new Account
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            ConnectorType = 308,
            NativeId = nativeId,
            AccountKind = accountKind,
            IsHuman = accountKind == "user",
            DisplayName = nativeId,
            SamAccountName = nativeId,
            Upn = $"{nativeId}@test.com",
            Email = $"{nativeId}@test.com",
            DomainName = "test.com",
            FilerName = "",
            NativeAccountId = nativeId,
            IsDeleted = false,
            IsDisabled = false,
            AccountTypeId = WellKnownAccountTypes.Unclassified,
            RawAttributes = rawAttributesJson,
            ContentHash = Guid.NewGuid().ToString(),
            HashVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
