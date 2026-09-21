using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Ingestion.AttributeMapping;
using Thor.Workflows.Ingestion.Models;
using Thor.Workflows.Ingestion.Normalization;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Normalization;

public class CyberArkNormalizerTests
{
    private static IngestBatch Normalize(string json) =>
        new CyberArkNormalizer(NullLogger<CyberArkNormalizer>.Instance, new AttributeMapProvider())
            .Normalize(Encoding.UTF8.GetBytes(json), Guid.NewGuid());

    [Fact]
    public void Safe_FieldsSourcedFromShippedAttributeMap()
    {
        var json = """
            {
              "Safes": [
                {"SafeNumber":7,"Location":"\\","SafeName":"PVWAReports"}
              ],
              "Accounts": { "AccountDetails": [] },
              "Members": []
            }
            """;

        var batch = Normalize(json);

        Assert.Single(batch.Assets);
        var asset = batch.Assets[0];
        Assert.Equal("7", asset.NativeId);
        Assert.Equal("PVWAReports", asset.DisplayName);
        Assert.Equal("cyberark_safe", asset.AssetType);
        Assert.Equal("PVWAReports", asset.FullPath);
        Assert.NotEmpty(asset.ContentHash);
    }

    [Fact]
    public void Safe_DroppedWhenSafeNumberIsDecimal()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":7.5,"SafeName":"PVWAReports"} ],
              "Accounts": { "AccountDetails": [] },
              "Members": []
            }
            """;

        var batch = Normalize(json);

        Assert.Empty(batch.Assets);
    }

    [Fact]
    public void Safe_RecoveredWhenSafeNumberIsQuotedString()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":"7","SafeName":"PVWAReports"} ],
              "Accounts": { "AccountDetails": [] },
              "Members": []
            }
            """;

        var batch = Normalize(json);

        Assert.Single(batch.Assets);
        Assert.Equal("7", batch.Assets[0].NativeId);
    }

    [Fact]
    public void Safe_DroppedWhenSafeNumberIsNonNumericString()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":"abc","SafeName":"PVWAReports"} ],
              "Accounts": { "AccountDetails": [] },
              "Members": []
            }
            """;

        var batch = Normalize(json);

        Assert.Empty(batch.Assets);
    }

    [Fact]
    public void Member_HasAccessEdgeDroppedWhenSafeNumberIsDecimal()
    {
        var json = """
            {
              "Safes": [], "Accounts": { "AccountDetails": [] },
              "Members": [
                {"SafeNumber":7.5,"MemberId":"17","MemberName":"PVWAAppUsers","MemberType":"Group","Permissions":{"ManageSafe":false,"ManageSafeMembers":false}}
              ]
            }
            """;

        var batch = Normalize(json);

        Assert.Single(batch.Groups);
        var edgeRefs = ((RawAttributesWithEdges)batch.Groups[0].RawAttributes).EdgeRefs;
        Assert.Empty(edgeRefs);
    }

    [Fact]
    public void Account_FieldsSourcedFromShippedAttributeMap()
    {
        var json = """
            {
              "Safes": [],
              "Accounts": {
                "AccountDetails": [
                  {"AccountId":"1005_6286","Name":"Operating System-svc_rancher_dmz","Address":"uatdomain.zone","UserName":"svc_rancher_dmz","SafeName":"AD Personal Admin Accounts"}
                ]
              },
              "Members": []
            }
            """;

        var batch = Normalize(json);

        Assert.Single(batch.Accounts);
        var account = batch.Accounts[0];
        Assert.Equal("1005_6286", account.NativeId);
        Assert.Equal("Operating System-svc_rancher_dmz", account.DisplayName);
        Assert.Equal("svc_rancher_dmz", account.SamAccountName);
        Assert.Equal("uatdomain.zone", account.DomainName);
        Assert.Equal("privileged", account.AccountKind);
        Assert.False(account.IsHuman);
        Assert.NotEmpty(account.ContentHash);
    }

    [Fact]
    public void Account_DroppedWhenNoAccountId()
    {
        var json = """
            {
              "Safes": [], "Accounts": { "AccountDetails": [ {"Name":"No Id Here"} ] }, "Members": []
            }
            """;

        var batch = Normalize(json);

        Assert.Empty(batch.Accounts);
    }

    [Fact]
    public void Account_DeclaresStoredInEdgeToItsSafe()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":7,"SafeName":"AD Personal Admin Accounts"} ],
              "Accounts": {
                "AccountDetails": [
                  {"AccountId":"1005_6286","SafeName":"AD Personal Admin Accounts"}
                ]
              },
              "Members": []
            }
            """;

        var batch = Normalize(json);

        var edgeRefs = ((RawAttributesWithEdges)batch.Accounts[0].RawAttributes).EdgeRefs;
        Assert.Single(edgeRefs);
        Assert.Equal(new EdgeRef("STORED_IN", "out", "7", "asset"), edgeRefs[0]);
    }

    [Fact]
    public void Account_NoStoredInEdge_WhenSafeNameDoesNotMatchAnySafe()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":7,"SafeName":"SomeOtherSafe"} ],
              "Accounts": { "AccountDetails": [ {"AccountId":"1","SafeName":"Unknown Safe"} ] },
              "Members": []
            }
            """;

        var batch = Normalize(json);

        var edgeRefs = ((RawAttributesWithEdges)batch.Accounts[0].RawAttributes).EdgeRefs;
        Assert.Empty(edgeRefs);
    }

    [Fact]
    public void Member_UserType_BecomesCyberArkScopedAccount()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":3,"SafeName":"Notification Engine"} ],
              "Accounts": { "AccountDetails": [] },
              "Members": [
                {"SafeNumber":3,"MemberId":"13","MemberName":"NotificationEngine","MemberType":"User","Permissions":{"ManageSafe":false,"ManageSafeMembers":false}}
              ]
            }
            """;

        var batch = Normalize(json);

        Assert.Single(batch.Accounts);
        var account = batch.Accounts[0];
        Assert.Equal("NotificationEngine", account.NativeId);
        Assert.Equal("NotificationEngine", account.DisplayName);
        Assert.Equal("cyberark_user", account.AccountKind);
        Assert.True(account.IsHuman);
        Assert.Empty(batch.Groups);
    }

    [Fact]
    public void Member_GroupType_BecomesCyberArkScopedGroup()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":7,"SafeName":"PVWAReports"} ],
              "Accounts": { "AccountDetails": [] },
              "Members": [
                {"SafeNumber":7,"MemberId":"17","MemberName":"PVWAAppUsers","MemberType":"Group","Permissions":{"ManageSafe":false,"ManageSafeMembers":true}}
              ]
            }
            """;

        var batch = Normalize(json);

        Assert.Empty(batch.Accounts);
        Assert.Single(batch.Groups);
        var group = batch.Groups[0];
        Assert.Equal("PVWAAppUsers", group.NativeId);
        Assert.Equal("cyberark_group", group.GroupClass);
    }

    [Fact]
    public void Member_HasAccessEdge_PropsCarryPermissionsPlusDerivedIsAdmin()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":7,"SafeName":"PVWAReports"} ],
              "Accounts": { "AccountDetails": [] },
              "Members": [
                {"SafeNumber":7,"MemberId":"17","MemberName":"PVWAAppUsers","MemberType":"Group","Permissions":{"ManageSafe":false,"ManageSafeMembers":true,"ListAccounts":true}}
              ]
            }
            """;

        var batch = Normalize(json);

        var edgeRefs = ((RawAttributesWithEdges)batch.Groups[0].RawAttributes).EdgeRefs;
        Assert.Single(edgeRefs);
        var edgeRef = edgeRefs[0];
        Assert.Equal("HAS_ACCESS", edgeRef.Rel);
        Assert.Equal("out", edgeRef.Dir);
        Assert.Equal("7", edgeRef.Key);
        Assert.Equal("asset", edgeRef.TargetType);

        using var props = JsonDocument.Parse(edgeRef.Props!);
        Assert.True(props.RootElement.GetProperty("is_admin").GetBoolean()); // ManageSafeMembers=true
        Assert.False(props.RootElement.GetProperty("ManageSafe").GetBoolean());
        Assert.True(props.RootElement.GetProperty("ListAccounts").GetBoolean());
    }

    [Fact]
    public void Member_SameIdentityAcrossMultipleSafes_ConsolidatesIntoOneEntityWithMultipleEdges()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":1,"SafeName":"SafeOne"}, {"SafeNumber":2,"SafeName":"SafeTwo"} ],
              "Accounts": { "AccountDetails": [] },
              "Members": [
                {"SafeNumber":1,"MemberId":"5","MemberName":"jdoe","MemberType":"User","Permissions":{"ManageSafe":false,"ManageSafeMembers":false}},
                {"SafeNumber":2,"MemberId":"9","MemberName":"jdoe","MemberType":"User","Permissions":{"ManageSafe":true,"ManageSafeMembers":false}}
              ]
            }
            """;

        var batch = Normalize(json);

        Assert.Single(batch.Accounts); // one consolidated entity, not one per safe
        var edgeRefs = ((RawAttributesWithEdges)batch.Accounts[0].RawAttributes).EdgeRefs;
        Assert.Equal(2, edgeRefs.Count);
        Assert.Contains(edgeRefs, r => r.Key == "1");
        Assert.Contains(edgeRefs, r => r.Key == "2");
    }

    [Fact]
    public void Member_DroppedWhenNoMemberTypeOrMemberName()
    {
        var json = """
            {
              "Safes": [], "Accounts": { "AccountDetails": [] },
              "Members": [ {"SafeNumber":1,"MemberId":"1"} ]
            }
            """;

        var batch = Normalize(json);

        Assert.Empty(batch.Accounts);
        Assert.Empty(batch.Groups);
    }

    [Fact]
    public void Normalize_ProducesNoEntitlements()
    {
        var json = """
            {
              "Safes": [ {"SafeNumber":1,"SafeName":"S"} ],
              "Accounts": { "AccountDetails": [ {"AccountId":"1"} ] },
              "Members": [ {"SafeNumber":1,"MemberId":"1","MemberName":"m","MemberType":"User"} ]
            }
            """;

        var batch = Normalize(json);

        Assert.Empty(batch.Entitlements);
    }
}
