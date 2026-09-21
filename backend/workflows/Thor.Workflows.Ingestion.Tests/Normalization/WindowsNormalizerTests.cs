using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Models;
using Thor.Workflows.Ingestion.Normalization;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Normalization;

public class WindowsNormalizerTests
{
    /// <summary>Builds a root export document with one Filer whose LocalADPaths holds the given accounts (mirrors AD's stringified-JSON-array convention).</summary>
    private static byte[] BuildExportBytes(params string[] accountsJson)
    {
        var innerJson = $"{{\"Accounts\":[{string.Join(",", accountsJson)}]}}";
        var filer = new JsonObject
        {
            ["ConnectorType"] = 1,
            ["LocalADPaths"] = new JsonArray(JsonValue.Create(innerJson)),
        };
        var root = new JsonObject { ["Filers"] = new JsonArray(filer) };
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static IngestBatch Normalize(params string[] accountsJson) =>
        new WindowsNormalizer(NullLogger<WindowsNormalizer>.Instance, new AttributeMapProvider())
            .Normalize(BuildExportBytes(accountsJson), Guid.NewGuid());

    [Fact]
    public void Account_FieldsSourcedFromShippedAttributeMap()
    {
        var json = """{"SamAccountName":"t.legros","Sid":"-1001","ObjectGuid":null,"Name":"Trevor Legros","ObjectClass":"User","Email":null,"DomainFQDN":"mark.stsdev03.com","AccountStatus":false}""";

        var batch = Normalize(json);

        Assert.Single(batch.Accounts);
        var account = batch.Accounts[0];
        Assert.Equal("Trevor Legros", account.DisplayName);
        Assert.Equal("t.legros", account.SamAccountName);
        Assert.Equal("mark.stsdev03.com", account.DomainName);
        Assert.Equal("-1001", account.NativeAccountId);
        Assert.Equal("user", account.AccountKind);
        Assert.True(account.IsHuman);
        Assert.NotEmpty(account.ContentHash);
    }

    [Fact]
    public void Account_NativeId_PrefersObjectGuidThenFallsBackToSid()
    {
        var withGuid = """{"SamAccountName":"has-guid","Sid":"-2001","ObjectGuid":"AAAA-BBBB","ObjectClass":"User"}""";
        var withoutGuid = """{"SamAccountName":"no-guid","Sid":"-2002","ObjectGuid":null,"ObjectClass":"User"}""";

        var batch = Normalize(withGuid, withoutGuid);

        Assert.Contains(batch.Accounts, a => a.NativeId == "aaaa-bbbb");
        Assert.Contains(batch.Accounts, a => a.NativeId == "-2002");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Account_IsDisabled_IsNegationOfAccountStatus(bool accountStatus, bool expectedIsDisabled)
    {
        var json = $$"""{"SamAccountName":"u","Sid":"-1","ObjectClass":"User","AccountStatus":{{accountStatus.ToString().ToLowerInvariant()}}}""";

        var batch = Normalize(json);

        Assert.Equal(expectedIsDisabled, batch.Accounts[0].IsDisabled);
    }

    [Fact]
    public void Normalize_DropsRecordWithNoObjectGuidOrSid()
    {
        var withSid = """{"SamAccountName":"a","Sid":"-1","ObjectClass":"User"}""";
        var withNeither = """{"SamAccountName":"b","Sid":null,"ObjectGuid":null,"ObjectClass":"User"}""";

        var batch = Normalize(withSid, withNeither);

        Assert.Single(batch.Accounts);
    }

    [Fact]
    public void Normalize_SplitsAccountsAndGroupsByObjectClass()
    {
        var userJson = """{"SamAccountName":"u1","Sid":"-1","ObjectClass":"User"}""";
        var groupJson = """{"SamAccountName":null,"Sid":"-500001","ObjectClass":"Group","Name":"Some Group"}""";

        var batch = Normalize(userJson, groupJson);

        Assert.Single(batch.Accounts);
        Assert.Single(batch.Groups);
        Assert.Equal("local_security", batch.Groups[0].GroupClass);
    }

    [Fact]
    public void Group_DirectMembers_BecomeEdgeRefsTargetingAccounts()
    {
        var groupJson = """
            {"SamAccountName":null,"Sid":"-500001","ObjectClass":"Group","Name":"Some Group",
             "DirectMembers":[{"SamAccountName":"m.rogahn"},{"SamAccountName":"a.kris"},{"SamAccountName":null}]}
            """;

        var batch = Normalize(groupJson);

        Assert.Single(batch.Groups);
        var edgeRefs = ((RawAttributesWithEdges)batch.Groups[0].RawAttributes).EdgeRefs;
        Assert.Equal(2, edgeRefs.Count);
        Assert.Contains(edgeRefs, r => r is { Rel: "MEMBER_OF", Dir: "in", Key: "m.rogahn", TargetType: "account" });
        Assert.Contains(edgeRefs, r => r is { Rel: "MEMBER_OF", Dir: "in", Key: "a.kris", TargetType: "account" });
    }

    [Fact]
    public void Account_AliasKey_IsLowercasedSamAccountName()
    {
        var json = """{"SamAccountName":"T.Legros","Sid":"-1001","ObjectClass":"User"}""";

        var batch = Normalize(json);

        var aliasKeys = ((RawAttributesWithEdges)batch.Accounts[0].RawAttributes).AliasKeys;
        Assert.Equal(["t.legros"], aliasKeys);
    }
}
