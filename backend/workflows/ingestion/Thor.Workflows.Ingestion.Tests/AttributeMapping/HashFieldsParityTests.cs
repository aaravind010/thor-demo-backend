using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Models;
using Thor.Workflows.Ingestion.Normalization;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.AttributeMapping;

/// <summary>
/// Guards against drift between each attribute-mapping JSON's <c>hashFields</c> list and the
/// corresponding normalizer's hand-written <c>HashableFields</c>-family dictionary.
/// <see cref="Hashing.ContentHasher.Hash"/> resolves a <c>hashFields</c> entry with no matching
/// dictionary key to a silent <c>null</c> (via <c>Dictionary.GetValueOrDefault</c>) rather than
/// failing — so a JSON-only edit would otherwise break CDC change-detection for that field with
/// no error anywhere. These tests fail fast instead.
/// </summary>
public class HashFieldsParityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "ActiveDirectory", "Account", (Func<HashSet<string>>)(() => AdNormalizer.HashableFields(DummyAccount(), []).Keys.ToHashSet()) };
        yield return new object[] { "ActiveDirectory", "Group", (Func<HashSet<string>>)(() => AdNormalizer.HashableFields(DummyGroup(), []).Keys.ToHashSet()) };
        yield return new object[] { "CyberArk", "Account", (Func<HashSet<string>>)(() => CyberArkNormalizer.PrivilegedAccountHashableFields(DummyAccount(), []).Keys.ToHashSet()) };
        yield return new object[] { "CyberArk", "Asset", (Func<HashSet<string>>)(() => CyberArkNormalizer.SafeHashableFields(DummyAsset()).Keys.ToHashSet()) };
        yield return new object[] { "CyberArk", "MemberAccount", (Func<HashSet<string>>)(() => CyberArkNormalizer.MemberAccountHashableFields(DummyAccount(), []).Keys.ToHashSet()) };
        yield return new object[] { "CyberArk", "MemberGroup", (Func<HashSet<string>>)(() => CyberArkNormalizer.MemberGroupHashableFields(DummyGroup(), []).Keys.ToHashSet()) };
        yield return new object[] { "Windows", "Account", (Func<HashSet<string>>)(() => WindowsNormalizer.HashableFields(DummyAccount()).Keys.ToHashSet()) };
        yield return new object[] { "Windows", "Group", (Func<HashSet<string>>)(() => WindowsNormalizer.HashableFields(DummyGroup(), []).Keys.ToHashSet()) };
        yield return new object[] { "HrFeed", "Identity", (Func<HashSet<string>>)(() => HrFeedNormalizer.HashableFields(DummyIdentity()).Keys.ToHashSet()) };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void HashFields_AllResolveToHashableFieldsKeys(string connector, string entityKind, Func<HashSet<string>> hashableFieldKeys)
    {
        var map = new AttributeMapProvider().GetMap(connector, entityKind);
        var keys = hashableFieldKeys();

        Assert.All(map.HashFields, field => Assert.Contains(field, keys));
    }

    private static ParsedIdentity DummyIdentity() => new(
        SourceId: Guid.NewGuid(),
        HrEmployeeId: "E1",
        DisplayName: "display",
        Email: "email",
        GivenName: "given",
        Surname: "surname",
        Department: "department",
        Title: "title",
        BusinessUnit: "business-unit",
        SamAccountName: "sam",
        Upn: "upn",
        ManagerEmployeeId: "E0",
        ManagerName: "manager",
        AdMatchField: "SamAccountName",
        AdMatchValue: "sam",
        MarkedToRetire: false,
        RawAttributes: new object());

    private static ParsedAccount DummyAccount() => new(
        SourceId: Guid.NewGuid(),
        ConnectorType: 0,
        NativeId: "native-id",
        AccountKind: "user",
        IsHuman: true,
        DisplayName: "display",
        SamAccountName: "sam",
        Upn: "upn",
        Email: "email",
        DomainName: "domain",
        NativeAccountId: "native-account-id",
        IsDeleted: false,
        IsDisabled: false,
        RawAttributes: new object());

    private static ParsedGroup DummyGroup() => new(
        SourceId: Guid.NewGuid(),
        ConnectorType: 0,
        NativeId: "native-id",
        GroupClass: "security_global",
        DisplayName: "display",
        Email: "email",
        DomainName: "domain",
        IsLargeGroup: false,
        IsDeleted: false,
        RawAttributes: new object());

    private static ParsedAsset DummyAsset() => new(
        SourceId: Guid.NewGuid(),
        ConnectorType: 0,
        NativeId: "native-id",
        AssetType: "cyberark_safe",
        DisplayName: "display",
        FullPath: "path",
        FilerName: null,
        FileSize: null,
        FileCount: null,
        BrokenAcl: null,
        IsProtected: null,
        RawAttributes: new object());
}
