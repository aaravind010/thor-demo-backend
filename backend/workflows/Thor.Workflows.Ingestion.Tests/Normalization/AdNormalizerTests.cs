using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Models;
using Thor.Workflows.Ingestion.Normalization;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Normalization;

/// <summary>
/// Net-new tests encoding §5.2/§5.3 field-mapping rules directly (there is no POC test file
/// for the normalizer itself — its correctness is exercised end-to-end in the POC's fixtures).
/// </summary>
public class AdNormalizerTests
{
    private static byte[] BuildExportBytes(params string[] accountsJson)
    {
        var innerJson = $"{{\"Domain\":\"test.com\",\"Accounts\":[{string.Join(",", accountsJson)}]}}";
        var domain = new JsonObject
        {
            ["ServerId"] = 1,
            ["AccountsPaths"] = new JsonArray(JsonValue.Create(innerJson)),
        };
        var root = new JsonObject { ["Domains"] = new JsonArray(domain) };
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static IngestBatch Normalize(params string[] accountsJson) =>
        Normalize(new AttributeMapProvider(), accountsJson);

    private static IngestBatch Normalize(IAttributeMapProvider mapProvider, params string[] accountsJson) =>
        new AdNormalizer(NullLogger<AdNormalizer>.Instance, mapProvider).Normalize(BuildExportBytes(accountsJson), Guid.NewGuid());

    [Fact]
    public void DomainName_DerivationDiffersBetweenAccountAndGroup()
    {
        var accountJson = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"],\"dn\":[\"CN=User1,OU=Sales,DC=Test,DC=COM\"]}";
        var groupJson = "{\"objectGUID\":[\"guid-2\"],\"objectClass\":[\"group\"],\"dn\":[\"CN=Grp1,OU=Sales,DC=Test,DC=COM\"]}";

        var batch = Normalize(accountJson, groupJson);

        // Account: tail after the first ",DC=", remaining ",DC=" replaced with "." — case preserved.
        Assert.Equal("Test.COM", batch.Accounts[0].DomainName);
        // Group: every "DC=" component joined with ".", lowercased.
        Assert.Equal("test.com", batch.Groups[0].DomainName);
    }

    [Theory]
    [InlineData("514", true)]   // 514 = 0x202, ACCOUNTDISABLE bit set
    [InlineData("512", false)]  // 512 = 0x200, bit not set
    [InlineData("notanumber", false)]
    public void Account_IsDisabled_BitCheck(string userAccountControl, bool expected)
    {
        var json = $"{{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"],\"userAccountControl\":[\"{userAccountControl}\"]}}";
        var batch = Normalize(json);
        Assert.Equal(expected, batch.Accounts[0].IsDisabled);
    }

    [Fact]
    public void Account_IsDisabled_DefaultsFalse_WhenAttributeAbsent()
    {
        var json = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"]}";
        var batch = Normalize(json);
        Assert.False(batch.Accounts[0].IsDisabled);
    }

    [Theory]
    [InlineData("-2147483646", false, "security_global")]
    [InlineData("-2147483644", false, "security_domainlocal")]
    [InlineData("-2147483640", false, "security_universal")]
    [InlineData("2", false, "distribution_global")]
    [InlineData("4", false, "distribution_domainlocal")]
    [InlineData("8", false, "distribution_universal")]
    [InlineData("999999", false, "security_global")]
    [InlineData("2", true, "distribution")]
    public void Group_GroupClass_BitmaskDecode(string groupType, bool isDistributionList, string expected)
    {
        var json = $"{{\"objectGUID\":[\"guid-2\"],\"objectClass\":[\"group\"],\"groupType\":[\"{groupType}\"],\"IsDL\":{(isDistributionList ? "true" : "false")}}}";
        var batch = Normalize(json);
        Assert.Equal(expected, batch.Groups[0].GroupClass);
    }

    [Fact]
    public void Account_EdgeRefs_MatchAdReferenceTable()
    {
        var json = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"],"
            + "\"memberOf\":[\"CN=Grp1,DC=test,DC=com\",\"CN=Grp2,DC=test,DC=com\"],"
            + "\"manager\":[\"CN=Boss,DC=test,DC=com\"]}";

        var batch = Normalize(json);
        var refs = ((RawAttributesWithEdges)batch.Accounts[0].RawAttributes).EdgeRefs;

        Assert.Equal(3, refs.Count);
        Assert.Contains(refs, r => r is { Rel: "MEMBER_OF", Dir: "out", Key: "cn=grp1,dc=test,dc=com", TargetType: "grp" });
        Assert.Contains(refs, r => r is { Rel: "MEMBER_OF", Dir: "out", Key: "cn=grp2,dc=test,dc=com", TargetType: "grp" });
        Assert.Contains(refs, r => r is { Rel: "REPORTS_TO", Dir: "out", Key: "cn=boss,dc=test,dc=com", TargetType: "account" });
    }

    [Fact]
    public void Group_EdgeRefs_MatchAdReferenceTable()
    {
        var json = "{\"objectGUID\":[\"guid-2\"],\"objectClass\":[\"group\"],"
            + "\"member\":[\"CN=User1,DC=test,DC=com\"],"
            + "\"memberOf\":[\"CN=ParentGrp,DC=test,DC=com\"],"
            + "\"managedBy\":[\"CN=Manager1,DC=test,DC=com\"]}";

        var batch = Normalize(json);
        var refs = ((RawAttributesWithEdges)batch.Groups[0].RawAttributes).EdgeRefs;

        Assert.Equal(3, refs.Count);
        Assert.Contains(refs, r => r is { Rel: "MEMBER_OF", Dir: "in", Key: "cn=user1,dc=test,dc=com", TargetType: null });
        Assert.Contains(refs, r => r is { Rel: "MEMBER_OF", Dir: "out", Key: "cn=parentgrp,dc=test,dc=com", TargetType: null });
        Assert.Contains(refs, r => r is { Rel: "MANAGED_BY", Dir: "out", Key: "cn=manager1,dc=test,dc=com", TargetType: null });
    }

    [Fact]
    public void Normalize_DropsRecordWithNoObjectGuid()
    {
        var withGuid = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"]}";
        var withoutGuid = "{\"objectClass\":[\"user\"]}";

        var batch = Normalize(withGuid, withoutGuid);

        Assert.Single(batch.Accounts);
    }

    [Fact]
    public void Normalize_SplitsAccountsAndGroupsByObjectClass()
    {
        var accountJson = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"]}";
        var groupJson = "{\"objectGUID\":[\"guid-2\"],\"objectClass\":[\"group\"]}";
        var computerJson = "{\"objectGUID\":[\"guid-3\"],\"objectClass\":[\"computer\"]}";

        var batch = Normalize(accountJson, groupJson, computerJson);

        Assert.Equal(2, batch.Accounts.Count);
        Assert.Single(batch.Groups);
        Assert.Contains(batch.Accounts, a => a.AccountKind == "computer" && !a.IsHuman);
        Assert.Contains(batch.Accounts, a => a.AccountKind == "user" && a.IsHuman);
    }

    [Fact]
    public void Normalize_ComputesContentHashAfterEveryOtherField()
    {
        var json = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"],\"displayName\":[\"User One\"]}";
        var batch = Normalize(json);
        Assert.NotEmpty(batch.Accounts[0].ContentHash);
    }

    [Fact]
    public void Account_DisplayName_SourcedFromShippedAttributeMap()
    {
        var json = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"],\"displayName\":[\"User One\"]}";
        var batch = Normalize(json);
        Assert.Equal("User One", batch.Accounts[0].DisplayName);
    }

    [Fact]
    public void Account_DisplayName_IsMapDriven_NotHardcoded()
    {
        // A map that renames display_name's source attribute to a made-up name should change
        // which raw key AdNormalizer reads — proving the wiring is genuinely config-driven.
        var configDir = Path.Combine(Path.GetTempPath(), "thor-attrmap-test-" + Guid.NewGuid());
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "ad-account.json"), """
                {
                  "connectorType": 308,
                  "entityKind": "Account",
                  "columnMappings": { "display_name": "customDisplayAttr" },
                  "hashFields": ["native_id"]
                }
                """);
            File.WriteAllText(Path.Combine(configDir, "ad-group.json"), """
                {
                  "connectorType": 308,
                  "entityKind": "Group",
                  "columnMappings": {},
                  "hashFields": ["native_id"]
                }
                """);
            var mapProvider = new AttributeMapProvider(configDir);

            var json = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"],"
                + "\"displayName\":[\"Ignored\"],\"customDisplayAttr\":[\"From Custom Attr\"]}";
            var batch = Normalize(mapProvider, json);

            Assert.Equal("From Custom Attr", batch.Accounts[0].DisplayName);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    [Fact]
    public void Account_UnmappedAttributes_FlowIntoExtra()
    {
        var json = "{\"objectGUID\":[\"guid-1\"],\"objectClass\":[\"user\"],"
            + "\"department\":[\"Engineering\"],\"employeeID\":[\"E123\"]}";

        var batch = Normalize(json);
        var extra = ((RawAttributesWithEdges)batch.Accounts[0].RawAttributes).Extra;

        Assert.Equal("Engineering", extra["department"]?.AsArray()[0]?.GetValue<string>());
        Assert.Equal("E123", extra["employeeID"]?.AsArray()[0]?.GetValue<string>());
    }

    [Fact]
    public void Group_UnmappedAttributes_FlowIntoExtra()
    {
        var json = "{\"objectGUID\":[\"guid-2\"],\"objectClass\":[\"group\"],"
            + "\"managedBy\":[\"CN=Manager1,DC=test,DC=com\"]}";

        var batch = Normalize(json);
        var extra = ((RawAttributesWithEdges)batch.Groups[0].RawAttributes).Extra;

        // managedBy feeds an edgeRef but isn't in ad-group.json's columnMappings, so it also
        // lands in Extra like any other unmapped attribute.
        Assert.Equal("CN=Manager1,DC=test,DC=com", extra["managedBy"]?.AsArray()[0]?.GetValue<string>());
    }
}
