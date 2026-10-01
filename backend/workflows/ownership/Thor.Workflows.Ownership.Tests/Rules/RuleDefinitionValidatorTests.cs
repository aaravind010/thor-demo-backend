using Thor.Rules.Ownership;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Rules;

/// <summary>Pure unit tests for <see cref="RuleDefinitionValidator"/> — no database needed.</summary>
public sealed class RuleDefinitionValidatorTests
{
    [Fact]
    public void UnsupportedSchemaVersion_IsRejected()
    {
        var payload = new RuleDefinitionPayload { SchemaVersion = 2, Field = "email", How = "exact" };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "account", payload, out var error));
        Assert.Contains("schemaVersion", error);
    }

    [Fact]
    public void UnrecognizedRuleType_IsRejected()
    {
        var payload = new RuleDefinitionPayload();

        Assert.False(RuleDefinitionValidator.TryValidate("some_new_kind", "account", payload, out var error));
        Assert.Contains("rule_type", error);
    }

    [Fact]
    public void FieldMatch_SimpleFieldMatch_IsAccepted()
    {
        var payload = new RuleDefinitionPayload { Field = "email", How = "exact" };

        Assert.True(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "account", payload, out var error));
        Assert.Equal("", error);
    }

    [Fact]
    public void FieldMatch_UnrecognizedField_IsRejected()
    {
        var payload = new RuleDefinitionPayload { Field = "not_a_real_field", How = "exact" };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "account", payload, out var error));
        Assert.Contains("unrecognized field", error);
    }

    [Fact]
    public void FieldMatch_UnrecognizedFieldForEntityType_IsRejected()
    {
        // "department" is a real logical field, but grp doesn't carry it in the catalog.
        var payload = new RuleDefinitionPayload { Field = "department", How = "exact" };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "grp", payload, out _));
    }

    [Fact]
    public void FieldMatch_UnrecognizedHow_IsRejected()
    {
        var payload = new RuleDefinitionPayload { Field = "email", How = "fuzzy_typo" };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "account", payload, out var error));
        Assert.Contains("how", error);
    }

    [Fact]
    public void FieldMatch_NegativeCap_IsRejected()
    {
        var payload = new RuleDefinitionPayload { Field = "department", How = "exact", Cap = 0 };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "account", payload, out var error));
        Assert.Contains("cap", error);
    }

    [Fact]
    public void FieldMatch_ViaHopNotAllowlisted_IsRejected()
    {
        var payload = new RuleDefinitionPayload
        {
            Via = [new EdgeHop { RelType = "SOME_UNKNOWN_REL", Direction = "out", ToType = "account" }],
            Field = "email", How = "exact",
        };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "account", payload, out var error));
        Assert.Contains("allowlisted", error);
    }

    [Fact]
    public void FieldMatch_ViaThenField_IsAccepted()
    {
        var payload = new RuleDefinitionPayload
        {
            Via = [new EdgeHop { RelType = "REPORTS_TO", Direction = "out", ToType = "account" }],
            Field = "email", How = "exact",
        };

        Assert.True(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "account", payload, out var error));
        Assert.Equal("", error);
    }

    [Fact]
    public void FieldMatch_HopThatFansOutIsNotLastHop_IsRejected()
    {
        var payload = new RuleDefinitionPayload
        {
            Via =
            [
                new EdgeHop { RelType = "HAS_ACCESS", Direction = "in", FromTypes = ["account", "grp"], ToType = "asset" },
                new EdgeHop { RelType = "REPORTS_TO", Direction = "out", ToType = "account" },
            ],
            Field = "email", How = "exact",
        };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "asset", payload, out var error));
        Assert.Contains("last hop", error);
    }

    [Fact]
    public void FieldMatch_TwoHopChain_IsAccepted()
    {
        var payload = new RuleDefinitionPayload
        {
            Via =
            [
                new EdgeHop { RelType = "STORED_IN", Direction = "out", ToType = "asset" },
                new EdgeHop { RelType = "HAS_ACCESS", Direction = "in", FromTypes = ["account", "grp"] },
            ],
            Field = "email", How = "exact",
        };

        Assert.True(RuleDefinitionValidator.TryValidate(OwnershipRuleType.FieldMatch, "account", payload, out var error));
        Assert.Equal("", error);
    }

    [Fact]
    public void SiblingMatch_WithVia_IsRejected()
    {
        var payload = new RuleDefinitionPayload
        {
            Via = [new EdgeHop { RelType = "REPORTS_TO", Direction = "out", ToType = "account" }],
            Field = "display_name", How = "exact",
        };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.SiblingMatch, "account", payload, out var error));
        Assert.Contains("via", error);
    }

    [Fact]
    public void SiblingMatch_SimpleField_IsAccepted()
    {
        var payload = new RuleDefinitionPayload { Field = "display_name", How = "similar" };

        Assert.True(RuleDefinitionValidator.TryValidate(OwnershipRuleType.SiblingMatch, "account", payload, out var error));
        Assert.Equal("", error);
    }

    [Fact]
    public void InheritOwner_NeitherViaNorWalk_IsRejected()
    {
        var payload = new RuleDefinitionPayload();

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.InheritOwner, "grp", payload, out var error));
        Assert.Contains("exactly one", error);
    }

    [Fact]
    public void InheritOwner_BothViaAndWalk_IsRejected()
    {
        var payload = new RuleDefinitionPayload
        {
            Via = [new EdgeHop { RelType = "MANAGED_BY", Direction = "out", ToType = "account" }],
            Walk = new WalkSpec { RelType = "MEMBER_OF", Direction = "out", ToType = "grp" },
        };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.InheritOwner, "grp", payload, out var error));
        Assert.Contains("exactly one", error);
    }

    [Fact]
    public void InheritOwner_ViaHop_IsAccepted()
    {
        var payload = new RuleDefinitionPayload { Via = [new EdgeHop { RelType = "MANAGED_BY", Direction = "out", ToType = "account" }] };

        Assert.True(RuleDefinitionValidator.TryValidate(OwnershipRuleType.InheritOwner, "grp", payload, out var error));
        Assert.Equal("", error);
    }

    [Fact]
    public void InheritOwner_WalkEntityTypeMustMatchAppliesTo()
    {
        // MEMBER_OF grp->grp is allowlisted, but walk.toType defaults to appliesTo — asking for
        // "grp" while applies_to is "account" isn't a same-type ancestor walk at all.
        var payload = new RuleDefinitionPayload { Walk = new WalkSpec { RelType = "MEMBER_OF", Direction = "out", ToType = "grp" } };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.InheritOwner, "account", payload, out _));
    }

    [Fact]
    public void InheritOwner_WalkForeignKeyColumnNotAllowlisted_IsRejected()
    {
        var payload = new RuleDefinitionPayload { Walk = new WalkSpec { Column = "some_other_column" } };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.InheritOwner, "asset", payload, out _));
    }

    [Fact]
    public void InheritOwner_WalkMaxDepthOutOfRange_IsRejected()
    {
        var payload = new RuleDefinitionPayload { Walk = new WalkSpec { Column = "parent_asset_id" }, MaxDepth = 999 };

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.InheritOwner, "asset", payload, out var error));
        Assert.Contains("maxDepth", error);
    }

    [Fact]
    public void MajorityOwner_MissingVia_IsRejected()
    {
        var payload = new RuleDefinitionPayload();

        Assert.False(RuleDefinitionValidator.TryValidate(OwnershipRuleType.MajorityOwner, "grp", payload, out var error));
        Assert.Contains("via", error);
    }

    [Fact]
    public void MajorityOwner_TwoHopChain_IsAccepted()
    {
        var payload = new RuleDefinitionPayload
        {
            Via =
            [
                new EdgeHop { RelType = "MEMBER_OF", Direction = "in", FromType = "account" },
                new EdgeHop { RelType = "REPORTS_TO", Direction = "out", ToType = "account" },
            ],
        };

        Assert.True(RuleDefinitionValidator.TryValidate(OwnershipRuleType.MajorityOwner, "grp", payload, out var error));
        Assert.Equal("", error);
    }

    private const string HasAccessPreferJson =
        """{"via":{"relType":"HAS_ACCESS","direction":"in","fromTypes":["account","grp"]},"prefer":{"prop":"is_admin"__VALUE__}}""";

    /// <summary>The compiler binds prefer.value with GetBoolean(), so anything but a JSON boolean must be rejected up front.</summary>
    [Theory]
    [InlineData(",\"value\":\"true\"")]
    [InlineData(",\"value\":1")]
    [InlineData(",\"value\":null")]
    [InlineData("")]
    public void InheritOwner_PreferValueNotBoolean_IsRejected(string valueFragment)
    {
        var json = HasAccessPreferJson.Replace("__VALUE__", valueFragment);

        Assert.False(OwnershipRuleDefinition.TryParse(OwnershipRuleType.InheritOwner, "asset", json, out _, out var error));
        Assert.Contains("prefer.value", error);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void InheritOwner_PreferValueBoolean_IsAccepted(string value)
    {
        var json = HasAccessPreferJson.Replace("__VALUE__", $",\"value\":{value}");

        Assert.True(OwnershipRuleDefinition.TryParse(OwnershipRuleType.InheritOwner, "asset", json, out _, out var error));
        Assert.Equal("", error);
    }
}
