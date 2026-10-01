using System.Text.Json;
using Thor.Rules.AccountType;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Rules;

public sealed class RuleNodeValidatorTests
{
    private static RuleNode Node(string json) => JsonSerializer.Deserialize<RuleNode>(json)!;

    [Theory]
    [InlineData("""{"field":"account_kind","operator":"equals","value":"user"}""")]
    [InlineData("""{"and":[{"field":"a","operator":"equals","value":"1"},{"field":"b","operator":"equals","value":"2"}]}""")]
    [InlineData("""{"or":[{"field":"a","operator":"equals","value":"1"},{"field":"b","operator":"equals","value":"2"}]}""")]
    [InlineData("""
        {"or":[
            {"and":[
                {"field":"a","operator":"equals","value":"1"},
                {"field":"b","operator":"contains","value":"2"}
            ]},
            {"field":"c","operator":"is_null"}
        ]}
        """)]
    public void IsValid_WellFormedShapes_ReturnsTrue(string json)
    {
        Assert.True(RuleNodeValidator.IsValid(Node(json), out var error));
        Assert.Empty(error);
    }

    [Fact]
    public void IsValid_BothAndAndOrOnSameNode_ReturnsFalse()
    {
        var node = Node("""
            {
                "and":[{"field":"a","operator":"equals","value":"1"}],
                "or":[{"field":"b","operator":"equals","value":"2"}]
            }
            """);

        Assert.False(RuleNodeValidator.IsValid(node, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValid_NeitherCompoundNorField_ReturnsFalse()
    {
        var node = Node("{}");

        Assert.False(RuleNodeValidator.IsValid(node, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValid_EmptyAnd_ReturnsFalse()
    {
        var node = Node("""{"and":[]}""");

        Assert.False(RuleNodeValidator.IsValid(node, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValid_EmptyOr_ReturnsFalse()
    {
        var node = Node("""{"or":[]}""");

        Assert.False(RuleNodeValidator.IsValid(node, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValid_EmptyAndBuriedInOtherwiseValidTree_ReturnsFalse()
    {
        var node = Node("""
            {"or":[
                {"and":[]},
                {"field":"c","operator":"is_null"}
            ]}
            """);

        Assert.False(RuleNodeValidator.IsValid(node, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValid_UnrecognizedOperator_ReturnsFalse()
    {
        var node = Node("""{"field":"account_kind","operator":"bogus_operator","value":"user"}""");

        Assert.False(RuleNodeValidator.IsValid(node, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValid_MissingOperatorOnLeaf_ReturnsFalse()
    {
        var node = Node("""{"field":"account_kind","value":"user"}""");

        Assert.False(RuleNodeValidator.IsValid(node, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void IsValid_InvalidGrandchildDeepInOtherwiseValidTree_ReturnsFalse()
    {
        var node = Node("""
            {"or":[
                {"and":[
                    {"field":"a","operator":"equals","value":"1"},
                    {"field":"b","operator":"totally_bogus","value":"2"}
                ]},
                {"field":"c","operator":"is_null"}
            ]}
            """);

        Assert.False(RuleNodeValidator.IsValid(node, out var error));
        Assert.NotEmpty(error);
    }
}
