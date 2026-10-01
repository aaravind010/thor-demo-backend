using System.Text.Json;
using Thor.DataLayer.Models.Tenants;
using Thor.Rules.AccountType;
using Thor.Workflows.Atre.Rules;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Rules;

public sealed class RuleEvaluatorTests
{
    private static Account NewAccount(string accountKind = "user", string rawAttributes = "{}") => new()
    {
        Id = Guid.NewGuid(),
        SourceId = Guid.NewGuid(),
        ConnectorType = 308,
        NativeId = "n1",
        AccountKind = accountKind,
        IsHuman = accountKind == "user",
        DisplayName = "Test",
        SamAccountName = "n1",
        Upn = "n1@test.com",
        Email = "n1@test.com",
        DomainName = "test.com",
        FilerName = "",
        NativeAccountId = "n1",
        IsDeleted = false,
        IsDisabled = false,
        AccountTypeId = Guid.NewGuid(),
        RawAttributes = rawAttributes,
        ContentHash = "hash",
        HashVersion = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static RuleNode Node(string json) => JsonSerializer.Deserialize<RuleNode>(json)!;

    [Theory]
    [InlineData("""{"field":"account_kind","operator":"equals","value":"user"}""", "user", true)]
    [InlineData("""{"field":"account_kind","operator":"equals","value":"user"}""", "computer", false)]
    [InlineData("""{"field":"account_kind","operator":"not_equals","value":"user"}""", "computer", true)]
    [InlineData("""{"field":"account_kind","operator":"starts_with","value":"comp"}""", "computer", true)]
    [InlineData("""{"field":"account_kind","operator":"ends_with","value":"uter"}""", "computer", true)]
    [InlineData("""{"field":"account_kind","operator":"contains","value":"ompu"}""", "computer", true)]
    [InlineData("""{"field":"account_kind","operator":"bogus_operator","value":"user"}""", "user", false)]
    public void Evaluate_LeafOperators_MatchExpected(string ruleJson, string accountKind, bool expected)
    {
        Assert.Equal(expected, RuleEvaluator.Evaluate(Node(ruleJson), NewAccount(accountKind)));
    }

    [Fact]
    public void Evaluate_CaseInsensitive_BothSidesLowercased()
    {
        var account = NewAccount("USER");
        var node = Node("""{"field":"account_kind","operator":"equals","value":"User"}""");
        Assert.True(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_IsNull_TrueWhenFieldMissingFromBoth()
    {
        var node = Node("""{"field":"department","operator":"is_null"}""");
        Assert.True(RuleEvaluator.Evaluate(node, NewAccount(rawAttributes: "{}")));
    }

    [Fact]
    public void Evaluate_IsNotNull_TrueWhenRawAttributeFieldPresent()
    {
        var node = Node("""{"field":"department","operator":"is_not_null"}""");
        Assert.True(RuleEvaluator.Evaluate(node, NewAccount(rawAttributes: """{"department":"Engineering"}""")));
    }

    [Fact]
    public void Evaluate_RawAttributesFallback_ContainsMatch()
    {
        var account = NewAccount(rawAttributes: """{"dn":"cn=svc1,ou=ServiceAccounts,dc=test"}""");
        var node = Node("""{"field":"dn","operator":"contains","value":"ou=serviceaccounts"}""");
        Assert.True(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_InvalidRegex_ReturnsFalseNotThrows()
    {
        var account = NewAccount(rawAttributes: """{"employeeID":"ABC12345"}""");
        var node = Node("""{"field":"employeeID","operator":"regex","value":"[a-z(unclosed"}""");
        Assert.False(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_ValidRegex_UnanchoredSubstringMatch()
    {
        var account = NewAccount(rawAttributes: """{"employeeID":"ABC12345"}""");
        var node = Node("""{"field":"employeeID","operator":"regex","value":"\\d{5}"}""");
        Assert.True(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_And_AllMustMatch()
    {
        var account = NewAccount("user", """{"dn":"cn=x,ou=serviceaccounts,dc=test"}""");
        var node = Node("""
            {"and":[
                {"field":"account_kind","operator":"equals","value":"user"},
                {"field":"dn","operator":"contains","value":"ou=serviceaccounts"}
            ]}
            """);
        Assert.True(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_And_OneFalseMakesWholeFalse()
    {
        var account = NewAccount("user", """{"dn":"cn=x,dc=test"}""");
        var node = Node("""
            {"and":[
                {"field":"account_kind","operator":"equals","value":"user"},
                {"field":"dn","operator":"contains","value":"ou=serviceaccounts"}
            ]}
            """);
        Assert.False(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_Or_AnyMatchIsEnough()
    {
        var account = NewAccount("computer");
        var node = Node("""
            {"or":[
                {"field":"account_kind","operator":"equals","value":"user"},
                {"field":"account_kind","operator":"equals","value":"computer"}
            ]}
            """);
        Assert.True(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_Or_NoneMatchIsFalse()
    {
        var account = NewAccount("service_principal");
        var node = Node("""
            {"or":[
                {"field":"account_kind","operator":"equals","value":"user"},
                {"field":"account_kind","operator":"equals","value":"computer"}
            ]}
            """);
        Assert.False(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_And_ThreeItemArray_AllMustMatch()
    {
        var account = NewAccount("user", """{"dn":"cn=x,ou=serviceaccounts,dc=test","department":"Engineering"}""");
        var node = Node("""
            {"and":[
                {"field":"account_kind","operator":"equals","value":"user"},
                {"field":"dn","operator":"contains","value":"ou=serviceaccounts"},
                {"field":"department","operator":"equals","value":"engineering"}
            ]}
            """);
        Assert.True(RuleEvaluator.Evaluate(node, account));
    }

    [Fact]
    public void Evaluate_OrContainingAndContainingOr_ThreeLevelsDeep()
    {
        // or( and(account_kind=user, dn contains ou=serviceaccounts), or(account_kind=computer) )
        var node = Node("""
            {"or":[
                {"and":[
                    {"field":"account_kind","operator":"equals","value":"user"},
                    {"field":"dn","operator":"contains","value":"ou=serviceaccounts"}
                ]},
                {"or":[
                    {"field":"account_kind","operator":"equals","value":"computer"}
                ]}
            ]}
            """);

        var matchesInnerAnd = NewAccount("user", """{"dn":"cn=x,ou=serviceaccounts,dc=test"}""");
        Assert.True(RuleEvaluator.Evaluate(node, matchesInnerAnd));

        var matchesInnerOr = NewAccount("computer");
        Assert.True(RuleEvaluator.Evaluate(node, matchesInnerOr));

        var matchesNeither = NewAccount("service_principal");
        Assert.False(RuleEvaluator.Evaluate(node, matchesNeither));
    }

    [Fact]
    public void Evaluate_MixedAndOrAtDifferentNodes_EvaluatesEachSubtreeIndependently()
    {
        // and( or(account_kind=user, account_kind=computer), dn contains ou=serviceaccounts )
        var node = Node("""
            {"and":[
                {"or":[
                    {"field":"account_kind","operator":"equals","value":"user"},
                    {"field":"account_kind","operator":"equals","value":"computer"}
                ]},
                {"field":"dn","operator":"contains","value":"ou=serviceaccounts"}
            ]}
            """);

        var matches = NewAccount("computer", """{"dn":"cn=x,ou=serviceaccounts,dc=test"}""");
        Assert.True(RuleEvaluator.Evaluate(node, matches));

        var wrongKind = NewAccount("service_principal", """{"dn":"cn=x,ou=serviceaccounts,dc=test"}""");
        Assert.False(RuleEvaluator.Evaluate(node, wrongKind));
    }
}
