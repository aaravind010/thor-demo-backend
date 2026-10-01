using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Atre.Rules;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Rules;

public sealed class AccountFactsTests
{
    private static Account NewAccount(string accountKind, string displayName, string rawAttributes) => new()
    {
        Id = Guid.NewGuid(),
        SourceId = Guid.NewGuid(),
        ConnectorType = 308,
        NativeId = "n1",
        AccountKind = accountKind,
        IsHuman = false,
        DisplayName = displayName,
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

    [Fact]
    public void Resolve_KnownColumnPresent_WinsOverRawAttributes()
    {
        var account = NewAccount("computer", "Real Name", """{"account_kind":"ignored"}""");
        Assert.Equal("computer", AccountFacts.Resolve(account, "account_kind"));
    }

    [Fact]
    public void Resolve_KnownColumnEmpty_FallsThroughToRawAttributes()
    {
        var account = NewAccount("user", displayName: "", rawAttributes: """{"display_name":"From Raw"}""");
        Assert.Equal("From Raw", AccountFacts.Resolve(account, "display_name"));
    }

    [Fact]
    public void Resolve_FieldNotKnownColumn_ReadsFromRawAttributes()
    {
        var account = NewAccount("user", "Name", """{"dn":"cn=x,dc=test"}""");
        Assert.Equal("cn=x,dc=test", AccountFacts.Resolve(account, "dn"));
    }

    [Fact]
    public void Resolve_FieldAbsentFromBoth_ReturnsNull()
    {
        var account = NewAccount("user", "Name", "{}");
        Assert.Null(AccountFacts.Resolve(account, "nonexistent_field"));
    }

    [Fact]
    public void Resolve_MalformedRawAttributes_ReturnsNullNotThrows()
    {
        var account = NewAccount("user", "", "{not valid json");
        Assert.Null(AccountFacts.Resolve(account, "display_name"));
    }

    [Fact]
    public void Resolve_IsHumanBooleanColumn_ReturnsLowercaseStringForm()
    {
        var account = NewAccount("user", "Name", "{}");
        account.IsHuman = true;
        Assert.Equal("true", AccountFacts.Resolve(account, "is_human"));
    }
}
