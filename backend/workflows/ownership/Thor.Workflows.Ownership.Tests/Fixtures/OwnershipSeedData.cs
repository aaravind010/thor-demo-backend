using System.Text.Json;
using Thor.DataLayer.Models.Tenants;
using Thor.Rules.Ownership;
using Thor.Workflows.Ownership.Rules;

namespace Thor.Workflows.Ownership.Tests.Fixtures;

internal static class OwnershipSeedData
{
    public const string EmailExactRule = OwnershipRuleDefaults.EmailExactMatch;
    public const string ManagerAttributeRule = OwnershipRuleDefaults.ManagerAttribute;
    public const string UpnCorrelationRule = OwnershipRuleDefaults.UpnCorrelation;
    public const string DisplayNameRule = OwnershipRuleDefaults.DisplayNameMatch;
    public const string DepartmentFallbackRule = OwnershipRuleDefaults.DepartmentFallback;
    public const string GroupManagedByRule = OwnershipRuleDefaults.GroupManagedByInheritance;
    public const string GroupNestingRule = OwnershipRuleDefaults.GroupNestingInheritance;
    public const string AssetParentRule = OwnershipRuleDefaults.AssetParentInheritance;
    public const string AssetCyberArkRule = OwnershipRuleDefaults.AssetCyberArkAccessInference;
    public const string EmployeeIdInAccountNameRule = OwnershipRuleDefaults.EmployeeIdInAccountName;
    public const string EmployeeIdInDisplayNameRule = OwnershipRuleDefaults.EmployeeIdInDisplayName;
    public const string CyberArkAccountViaSafeRule = OwnershipRuleDefaults.CyberArkAccountViaSafe;
    public const string SameAccountNameRule = OwnershipRuleDefaults.SameAccountName;
    public const string SimilarAccountNameRule = OwnershipRuleDefaults.SimilarAccountName;
    public const string MajorityManagerRule = OwnershipRuleDefaults.MajorityManager;
    public const string MajorityGroupOwnerRule = OwnershipRuleDefaults.MajorityGroupOwner;

    /// <summary>Builds the real built-in rules from <see cref="OwnershipRuleDefaults"/> — the same content <see cref="OwnershipRuleSeeder"/> would insert — so test fixtures can never drift from production seed data.</summary>
    public static List<OwnershipRule> BuildRules()
    {
        var now = DateTimeOffset.UtcNow;

        return OwnershipRuleDefaults.All.Select(seed => new OwnershipRule
        {
            Id = Guid.NewGuid(),
            RuleName = seed.RuleName,
            RuleType = seed.RuleType,
            RuleDefinition = seed.RuleDefinitionJson,
            AppliesTo = seed.AppliesTo,
            IsActive = seed.IsActive,
            PrecisionScore = seed.PrecisionScore,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();
    }

    public static IdentityRecord BuildIdentity(
        string hrEmployeeId, string displayName, string email, string? department = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new IdentityRecord
        {
            Id = Guid.NewGuid(),
            Source = "hr_feed",
            HrEmployeeId = hrEmployeeId,
            DisplayName = displayName,
            Email = email,
            GivenName = displayName.Split(' ')[0],
            Surname = displayName.Contains(' ') ? displayName.Split(' ')[1] : "",
            Department = department,
            IsActive = true,
            ContentHash = Guid.NewGuid().ToString(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary><paramref name="department"/> is written under the real "Department" (capital D) RawAttributes key — the key <c>Stager.cs</c> actually serializes in production.</summary>
    public static Account BuildAccount(
        Guid sourceId, string nativeId, string? email = null, string? upn = null,
        string? displayName = null, string? department = null)
    {
        var now = DateTimeOffset.UtcNow;
        var rawAttributesJson = department is null
            ? "{}"
            : JsonSerializer.Serialize(new Dictionary<string, string> { ["Department"] = department });

        return new Account
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            ConnectorType = 308,
            NativeId = nativeId,
            AccountKind = "user",
            IsHuman = true,
            DisplayName = displayName ?? nativeId,
            SamAccountName = nativeId,
            Upn = upn ?? $"{nativeId}@corp.local",
            Email = email ?? "",
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

    public static Grp BuildGroup(Guid sourceId, string nativeId, string? displayName = null, string? email = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Grp
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            ConnectorType = 308,
            NativeId = nativeId,
            GroupClass = "security",
            DisplayName = displayName ?? nativeId,
            Email = email,
            DomainName = "test.com",
            IsLargeGroup = false,
            IsDeleted = false,
            RawAttributes = "{}",
            ContentHash = Guid.NewGuid().ToString(),
            HashVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static Asset BuildAsset(Guid sourceId, string nativeId, string? displayName = null, Guid? parentAssetId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Asset
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            ConnectorType = 308,
            NativeId = nativeId,
            AssetType = "share",
            DisplayName = displayName ?? nativeId,
            FullPath = $"\\\\filer\\{nativeId}",
            FilerName = "filer01",
            FileSize = 0,
            FileCount = 0,
            BrokenAcl = false,
            IsProtected = false,
            ParentAssetId = parentAssetId,
            RawAttributes = "{}",
            ContentHash = Guid.NewGuid().ToString(),
            HashVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static Edge BuildEdge(Guid fromId, string fromType, Guid toId, string toType, string relType, string? props = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Edge
        {
            Id = Guid.NewGuid(),
            FromId = fromId,
            FromType = fromType,
            ToId = toId,
            ToType = toType,
            RelType = relType,
            Props = props,
            IsDeleted = false,
            ContentHash = Guid.NewGuid().ToString(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
