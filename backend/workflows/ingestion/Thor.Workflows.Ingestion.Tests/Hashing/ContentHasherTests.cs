using System.Text.Json.Nodes;
using Thor.Workflows.Ingestion.Hashing;
using Thor.Workflows.Ingestion.Models;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Hashing;

/// <summary>
/// Ports the account/group subset of the POC's test_cdc_hash.py. Asset/entitlement/edge
/// hashing is out of scope (no connector producing those types yet); the POC's
/// "extra untracked field is ignored" test has no equivalent here — a strongly-typed
/// C# record has no concept of an untracked extra field.
///
/// <see cref="ContentHasher.Hash"/> takes a plain field dictionary + an ordered field-name
/// list (an attribute map's <c>hashFields</c>), rather than a <c>ParsedAccount</c>/
/// <c>ParsedGroup</c> directly — these tests build that dictionary themselves, mirroring
/// <c>AdNormalizer</c>'s private <c>HashableFields</c> helpers, to keep the hasher itself
/// entity-agnostic.
/// </summary>
public class ContentHasherTests
{
    private static readonly IReadOnlyList<string> AccountHashFields =
        ["connector_type", "native_id", "account_kind", "display_name", "upn", "email", "domain_name", "is_deleted", "is_disabled", "edge_refs"];

    private static readonly IReadOnlyList<string> GroupHashFields =
        ["connector_type", "native_id", "group_class", "display_name", "email", "domain_name", "is_deleted", "edge_refs"];

    private static ParsedAccount BaseAccount() => new(
        SourceId: Guid.NewGuid(),
        ConnectorType: 308,
        NativeId: "cn=user1,dc=test",
        AccountKind: "user",
        IsHuman: true,
        DisplayName: "User One",
        SamAccountName: "user1",
        Upn: "user1@test.com",
        Email: "user1@test.com",
        DomainName: "test.com",
        NativeAccountId: "S-1-5-21-1",
        IsDeleted: false,
        IsDisabled: false,
        RawAttributes: new RawAttributesWithEdges(
            AliasKeys: ["cn=user1,dc=test"],
            EdgeRefs: [],
            Extra: new Dictionary<string, JsonNode?>()));

    private static ParsedGroup BaseGroup() => new(
        SourceId: Guid.NewGuid(),
        ConnectorType: 308,
        NativeId: "cn=grp1,dc=test",
        GroupClass: "security_global",
        DisplayName: "Group One",
        Email: null,
        DomainName: "test.com",
        IsLargeGroup: false,
        IsDeleted: false,
        RawAttributes: new RawAttributesWithEdges(
            AliasKeys: ["cn=grp1,dc=test"],
            EdgeRefs: [],
            Extra: new Dictionary<string, JsonNode?>()));

    private static Dictionary<string, object?> Fields(ParsedAccount a) => new()
    {
        ["connector_type"] = a.ConnectorType,
        ["native_id"] = a.NativeId,
        ["account_kind"] = a.AccountKind,
        ["display_name"] = a.DisplayName,
        ["upn"] = a.Upn,
        ["email"] = a.Email,
        ["domain_name"] = a.DomainName,
        ["is_deleted"] = a.IsDeleted,
        ["is_disabled"] = a.IsDisabled,
        ["edge_refs"] = ((RawAttributesWithEdges)a.RawAttributes).EdgeRefs,
    };

    private static Dictionary<string, object?> Fields(ParsedGroup g) => new()
    {
        ["connector_type"] = g.ConnectorType,
        ["native_id"] = g.NativeId,
        ["group_class"] = g.GroupClass,
        ["display_name"] = g.DisplayName,
        ["email"] = g.Email,
        ["domain_name"] = g.DomainName,
        ["is_deleted"] = g.IsDeleted,
        ["edge_refs"] = ((RawAttributesWithEdges)g.RawAttributes).EdgeRefs,
    };

    private static string AccountHash(ParsedAccount a) => ContentHasher.Hash(Fields(a), AccountHashFields);
    private static string GroupHash(ParsedGroup g) => ContentHasher.Hash(Fields(g), GroupHashFields);

    // ── Determinism ─────────────────────────────────────────────────────────

    [Fact]
    public void AccountHash_IsDeterministic()
    {
        Assert.Equal(AccountHash(BaseAccount()), AccountHash(BaseAccount()));
    }

    [Fact]
    public void GroupHash_IsDeterministic()
    {
        Assert.Equal(GroupHash(BaseGroup()), GroupHash(BaseGroup()));
    }

    // ── Account field sensitivity (ACCOUNT_HASH_FIELDS) ─────────────────────

    [Fact]
    public void AccountHash_SensitiveTo_ConnectorType()
    {
        var a = BaseAccount();
        var b = a with { ConnectorType = 309 };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_NativeId()
    {
        var a = BaseAccount();
        var b = a with { NativeId = "cn=other,dc=test" };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_AccountKind()
    {
        var a = BaseAccount();
        var b = a with { AccountKind = "computer" };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_DisplayName()
    {
        var a = BaseAccount();
        var b = a with { DisplayName = "Someone Else" };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_Upn()
    {
        var a = BaseAccount();
        var b = a with { Upn = "other@test.com" };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_Email()
    {
        var a = BaseAccount();
        var b = a with { Email = "other@test.com" };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_DomainName()
    {
        var a = BaseAccount();
        var b = a with { DomainName = "other.com" };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_IsDeleted()
    {
        var a = BaseAccount();
        var b = a with { IsDeleted = true };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_IsDisabled()
    {
        var a = BaseAccount();
        var b = a with { IsDisabled = true };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_SensitiveTo_EdgeRefsChange()
    {
        var a = BaseAccount();
        var b = a with
        {
            RawAttributes = ((RawAttributesWithEdges)a.RawAttributes) with
            {
                EdgeRefs = [new EdgeRef("MEMBER_OF", "out", "cn=grp1,dc=test", "grp")]
            }
        };
        Assert.NotEqual(AccountHash(a), AccountHash(b));
    }

    [Fact]
    public void AccountHash_NoneVsEmptyString_Differ()
    {
        var withNull = BaseAccount() with { Email = null };
        var withEmpty = BaseAccount() with { Email = "" };
        Assert.NotEqual(AccountHash(withNull), AccountHash(withEmpty));
    }

    [Fact]
    public void Hash_IgnoresFieldsNotListedInHashFields()
    {
        var a = BaseAccount();
        var b = a with { Email = "other@test.com" };
        var hashFieldsWithoutEmail = AccountHashFields.Where(f => f != "email").ToArray();

        Assert.Equal(
            ContentHasher.Hash(Fields(a), hashFieldsWithoutEmail),
            ContentHasher.Hash(Fields(b), hashFieldsWithoutEmail));
    }

    // ── Group field sensitivity (GROUP_HASH_FIELDS) ─────────────────────────

    [Fact]
    public void GroupHash_SensitiveTo_ConnectorType()
    {
        var a = BaseGroup();
        var b = a with { ConnectorType = 309 };
        Assert.NotEqual(GroupHash(a), GroupHash(b));
    }

    [Fact]
    public void GroupHash_SensitiveTo_NativeId()
    {
        var a = BaseGroup();
        var b = a with { NativeId = "cn=other,dc=test" };
        Assert.NotEqual(GroupHash(a), GroupHash(b));
    }

    [Fact]
    public void GroupHash_SensitiveTo_GroupClass()
    {
        var a = BaseGroup();
        var b = a with { GroupClass = "distribution" };
        Assert.NotEqual(GroupHash(a), GroupHash(b));
    }

    [Fact]
    public void GroupHash_SensitiveTo_DisplayName()
    {
        var a = BaseGroup();
        var b = a with { DisplayName = "Other Group" };
        Assert.NotEqual(GroupHash(a), GroupHash(b));
    }

    [Fact]
    public void GroupHash_SensitiveTo_Email()
    {
        var a = BaseGroup();
        var b = a with { Email = "grp1@test.com" };
        Assert.NotEqual(GroupHash(a), GroupHash(b));
    }

    [Fact]
    public void GroupHash_SensitiveTo_DomainName()
    {
        var a = BaseGroup();
        var b = a with { DomainName = "other.com" };
        Assert.NotEqual(GroupHash(a), GroupHash(b));
    }

    [Fact]
    public void GroupHash_SensitiveTo_IsDeleted()
    {
        var a = BaseGroup();
        var b = a with { IsDeleted = true };
        Assert.NotEqual(GroupHash(a), GroupHash(b));
    }

    [Fact]
    public void GroupHash_SensitiveTo_EdgeRefsChange()
    {
        var a = BaseGroup();
        var b = a with
        {
            RawAttributes = ((RawAttributesWithEdges)a.RawAttributes) with
            {
                EdgeRefs = [new EdgeRef("MANAGED_BY", "out", "cn=user1,dc=test")]
            }
        };
        Assert.NotEqual(GroupHash(a), GroupHash(b));
    }
}
