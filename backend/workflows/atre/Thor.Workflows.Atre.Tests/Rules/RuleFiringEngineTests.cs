using Microsoft.Extensions.Logging.Abstractions;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Atre.Rules;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Rules;

public sealed class RuleFiringEngineTests
{
    private static Account NewAccount(string accountKind) => new()
    {
        Id = Guid.NewGuid(),
        SourceId = Guid.NewGuid(),
        ConnectorType = 308,
        NativeId = "n1",
        AccountKind = accountKind,
        IsHuman = accountKind == "user",
        DisplayName = "n1",
        SamAccountName = "n1",
        Upn = "n1@test.com",
        Email = "n1@test.com",
        DomainName = "test.com",
        FilerName = "",
        NativeAccountId = "n1",
        IsDeleted = false,
        IsDisabled = false,
        AccountTypeId = Guid.NewGuid(),
        RawAttributes = "{}",
        ContentHash = "hash",
        HashVersion = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static AccountTypeRule NewRule(string ruleDefinition, Guid targetTypeId, decimal weight) => new()
    {
        Id = Guid.NewGuid(),
        RuleName = "test-rule",
        RuleDefinition = ruleDefinition,
        AppliesTo = "account",
        TargetAccountTypeId = targetTypeId,
        IsActive = true,
        PrecisionScore = weight,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Parse_MalformedRuleDefinition_SkipsThatRuleOnly()
    {
        var goodType = Guid.NewGuid();
        var rules = new List<AccountTypeRule>
        {
            NewRule("""{"field":"account_kind","operator":"equals","value":"user"}""", goodType, 0.5m),
            NewRule("{not valid json", Guid.NewGuid(), 0.5m),
        };

        var parsed = RuleFiringEngine.Parse(rules, NullLogger.Instance);

        Assert.Single(parsed);
        Assert.Equal(goodType, parsed[0].TargetAccountTypeId);
    }

    [Fact]
    public void Parse_InvalidShape_BothAndAndOr_SkipsThatRuleOnly()
    {
        var goodType = Guid.NewGuid();
        var rules = new List<AccountTypeRule>
        {
            NewRule("""{"field":"account_kind","operator":"equals","value":"user"}""", goodType, 0.5m),
            NewRule(
                """{"and":[{"field":"a","operator":"equals","value":"1"}],"or":[{"field":"b","operator":"equals","value":"2"}]}""",
                Guid.NewGuid(),
                0.5m),
        };

        var parsed = RuleFiringEngine.Parse(rules, NullLogger.Instance);

        Assert.Single(parsed);
        Assert.Equal(goodType, parsed[0].TargetAccountTypeId);
    }

    [Fact]
    public void Parse_InvalidShape_EmptyAnd_SkipsThatRuleOnly()
    {
        var goodType = Guid.NewGuid();
        var rules = new List<AccountTypeRule>
        {
            NewRule("""{"field":"account_kind","operator":"equals","value":"user"}""", goodType, 0.5m),
            NewRule("""{"and":[]}""", Guid.NewGuid(), 0.5m),
        };

        var parsed = RuleFiringEngine.Parse(rules, NullLogger.Instance);

        Assert.Single(parsed);
        Assert.Equal(goodType, parsed[0].TargetAccountTypeId);
    }

    [Fact]
    public void Parse_InvalidShape_EmptyOr_SkipsThatRuleOnly()
    {
        var goodType = Guid.NewGuid();
        var rules = new List<AccountTypeRule>
        {
            NewRule("""{"field":"account_kind","operator":"equals","value":"user"}""", goodType, 0.5m),
            NewRule("""{"or":[]}""", Guid.NewGuid(), 0.5m),
        };

        var parsed = RuleFiringEngine.Parse(rules, NullLogger.Instance);

        Assert.Single(parsed);
        Assert.Equal(goodType, parsed[0].TargetAccountTypeId);
    }

    [Fact]
    public void Evaluate_OneRuleFires_ReturnsOneVote()
    {
        var targetType = Guid.NewGuid();
        var rules = new List<AccountTypeRule>
        {
            NewRule("""{"field":"account_kind","operator":"equals","value":"user"}""", targetType, 0.55m),
        };
        var parsed = RuleFiringEngine.Parse(rules, NullLogger.Instance);

        var votes = RuleFiringEngine.Evaluate(parsed, NewAccount("user"), NullLogger.Instance);

        Assert.Single(votes);
        Assert.Equal(targetType, votes[0].TargetAccountTypeId);
        Assert.Equal(0.55m, votes[0].Weight);
    }

    [Fact]
    public void Evaluate_NoRuleFires_ReturnsEmpty()
    {
        var rules = new List<AccountTypeRule>
        {
            NewRule("""{"field":"account_kind","operator":"equals","value":"computer"}""", Guid.NewGuid(), 0.99m),
        };
        var parsed = RuleFiringEngine.Parse(rules, NullLogger.Instance);

        var votes = RuleFiringEngine.Evaluate(parsed, NewAccount("user"), NullLogger.Instance);

        Assert.Empty(votes);
    }
}
