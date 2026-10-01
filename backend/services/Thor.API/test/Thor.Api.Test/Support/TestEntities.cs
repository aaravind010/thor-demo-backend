using Thor.DataLayer.Models.Tenants;

namespace Thor.Api.Test.Support;

/// <summary>Minimal valid tenant rows (every required column set) for seeding the in-memory tenant database.</summary>
public static class TestEntities
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static AccountType AccountType(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), Name = "Service Account", Description = "Non-human", IsHuman = false,
    };

    public static Source Source(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), ConnectorType = 1, Name = "AD", Config = "{}", IsActive = true,
        CreatedAt = Now, UpdatedAt = Now,
    };

    public static Account Account(Guid sourceId, Guid accountTypeId, Guid? id = null, bool isDeleted = false) => new()
    {
        Id = id ?? Guid.NewGuid(), SourceId = sourceId, ConnectorType = 1, NativeId = Guid.NewGuid().ToString(),
        AccountKind = "user", IsHuman = true, DisplayName = "Jane Doe", SamAccountName = "jdoe",
        Upn = "jdoe@corp.example", Email = "jdoe@corp.example", DomainName = "corp", FilerName = "",
        NativeAccountId = "", IsDeleted = isDeleted, IsDisabled = false, AccountTypeId = accountTypeId,
        RawAttributes = """{"Department":"IT"}""", ContentHash = "h", HashVersion = 1, CreatedAt = Now, UpdatedAt = Now,
    };

    public static Grp Group(Guid sourceId, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), SourceId = sourceId, ConnectorType = 1, NativeId = Guid.NewGuid().ToString(),
        GroupClass = "security", DisplayName = "Admins", RawAttributes = """{"Scope":"Global"}""",
        HashVersion = 1, CreatedAt = Now, UpdatedAt = Now,
    };

    public static IdentityRecord Identity(Guid? id = null, bool isActive = true) => new()
    {
        Id = id ?? Guid.NewGuid(), Source = "hr", HrEmployeeId = "E1", DisplayName = "Jane Doe",
        Email = "jane@corp.example", GivenName = "Jane", Surname = "Doe", IsActive = isActive, ContentHash = "h",
        CreatedAt = Now, UpdatedAt = Now,
    };

    public static AccountTypeRule AccountTypeRule(Guid targetAccountTypeId, Guid? id = null, bool isActive = true) => new()
    {
        Id = id ?? Guid.NewGuid(), RuleName = "Service by kind", AppliesTo = "account",
        RuleDefinition = """{"field":"account_kind","operator":"equals","value":"service"}""",
        TargetAccountTypeId = targetAccountTypeId, IsActive = isActive, PrecisionScore = 0.8m,
        CreatedAt = Now, UpdatedAt = Now,
    };

    public static OwnershipRule OwnershipRule(string ruleName = "Custom email match", Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), RuleName = ruleName, RuleType = "field_match", AppliesTo = "account",
        RuleDefinition = """{"schemaVersion":1,"field":"email","how":"exact"}""", IsActive = true,
        PrecisionScore = 0.8m, CreatedAt = Now, UpdatedAt = Now,
    };

    public static PartyAssignment PartyAssignment(Guid entityId, Guid identityId, string runId = "run-1", Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), EntityType = "account", EntityId = entityId, IdentityId = identityId, Rank = 1,
        IsActive = true, VoteDistribution = "{}", ContributingRuleIds = [], PrecisionScoreSnapshot = "{}",
        RunId = runId, AssignedAt = Now,
    };

    public static AtreVote AccountVote(Guid entityId, Guid ruleId, Guid votedFor, string runId = "run-1", Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), EntityId = entityId, RuleId = ruleId, VotedFor = votedFor, VoteWeight = 0.8m,
        RunId = runId, CreatedAt = Now,
    };

    public static OwnershipVote OwnershipVote(Guid entityId, Guid ruleId, Guid votedFor, string runId = "run-1", Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), EntityId = entityId, RuleId = ruleId, VotedFor = votedFor, VoteWeight = 0.8m,
        RunId = runId, CreatedAt = Now,
    };
}
