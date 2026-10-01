using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.Support;
using Thor.DataConnectionManager;

namespace Thor.Api.Test.Controllers.V1;

public class AccountTypeRulesControllerTests
{
    private const string ValidDefinition = """{"field":"account_kind","operator":"equals","value":"service"}""";

    private readonly InMemoryTenant _tenant = new();
    private readonly Guid _accountTypeId = Guid.NewGuid();

    public AccountTypeRulesControllerTests() => _tenant.Seed(TestEntities.AccountType(_accountTypeId));

    private AccountTypeRulesController CreateController(
        ITenantConnectionManager? manager = null, bool includeTenantHeader = true, string? tenantHeaderValue = null) =>
        InMemoryTenant.WithTenantHeader(
            new AccountTypeRulesController(new AccountTypeRuleService(manager ?? _tenant.ConnectionManager())),
            includeTenantHeader, tenantHeaderValue);

    private CreateAccountTypeRuleRequest ValidRequest() => new(
        RuleName: "Service by kind", RuleDefinition: ValidDefinition, AppliesTo: "account",
        TargetAccountTypeId: _accountTypeId, PrecisionScore: 0.8m);

    [Fact]
    public async Task Create_MissingTenantHeader_ReturnsBadRequest()
    {
        var result = await CreateController(includeTenantHeader: false).Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_InvalidTenantHeader_ReturnsBadRequest()
    {
        var result = await CreateController(tenantHeaderValue: "not-a-guid").Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingRuleName_ReturnsBadRequest(string? ruleName)
    {
        var result = await CreateController().Create(ValidRequest() with { RuleName = ruleName! }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be("RuleName is required.");
    }

    /// <summary>A zero weight still fires but can never win, and weights above 1 aren't precision scores.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public async Task Create_PrecisionScoreOutOfRange_ReturnsBadRequest(double score)
    {
        var request = ValidRequest() with { PrecisionScore = (decimal)score };

        var result = await CreateController().Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("PrecisionScore must be greater than 0 and at most 1.");
    }

    /// <summary>ATRE only evaluates rules scoped to "account"; anything else would never run.</summary>
    [Fact]
    public async Task Create_AppliesToNotAccount_ReturnsBadRequest()
    {
        var result = await CreateController().Create(ValidRequest() with { AppliesTo = "grp" }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be("appliesTo must be 'account'.");
    }

    /// <summary>The same validator ATRE loads rules through rejects the definition, and its reason is surfaced.</summary>
    [Theory]
    [InlineData("""{"field":"account_kind","operator":"bogus","value":"x"}""", "unrecognized operator")]
    [InlineData("""{"and":[]}""", "empty 'and'")]
    [InlineData("""{"and":[{"field":"a","operator":"equals"}],"or":[{"field":"b","operator":"equals"}]}""", "both 'and' and 'or'")]
    [InlineData("not json", "malformed rule_definition")]
    [InlineData("null", "deserialized to null")]
    public async Task Create_InvalidDefinition_ReturnsBadRequestWithValidatorReason(string definition, string expectedReason)
    {
        var result = await CreateController().Create(ValidRequest() with { RuleDefinition = definition }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<string>().Which.Should().Contain(expectedReason);
        using var db = _tenant.CreateDbContext();
        db.AccountTypeRules.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_UnknownTargetAccountType_ReturnsBadRequest()
    {
        var request = ValidRequest() with { TargetAccountTypeId = Guid.NewGuid() };

        var result = await CreateController().Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<string>().Which.Should().StartWith("Account type not found");
    }

    [Fact]
    public async Task Create_TenantNotFound_ReturnsForbidden()
    {
        var result = await CreateController(InMemoryTenant.UnknownTenantConnectionManager())
            .Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Create_ValidRequest_PersistsRuleAndReturnsCreated()
    {
        var result = await CreateController().Create(ValidRequest(), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var response = objectResult.Value.Should().BeOfType<AccountTypeRuleResponse>().Subject;
        response.IsActive.Should().BeTrue();
        response.TotalPredictions.Should().Be(0);

        using var db = _tenant.CreateDbContext();
        var persisted = db.AccountTypeRules.Single(r => r.Id == response.Id);
        persisted.RuleDefinition.Should().Be(ValidDefinition);
        persisted.TargetAccountTypeId.Should().Be(_accountTypeId);
        persisted.PrecisionScore.Should().Be(0.8m);
        persisted.CreatedAt.Should().Be(persisted.UpdatedAt);
    }

    [Fact]
    public async Task List_FiltersByIsActive()
    {
        var active = TestEntities.AccountTypeRule(_accountTypeId);
        _tenant.Seed(active, TestEntities.AccountTypeRule(_accountTypeId, isActive: false));

        var result = await CreateController().List(appliesTo: null, isActive: true, targetAccountTypeId: null, after: null);

        var page = result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<AccountTypeRuleResponse>>().Subject;
        page.Items.Select(r => r.Id).Should().Equal(active.Id);
        page.NextCursor.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task List_LimitOutOfRange_ReturnsBadRequest(int limit)
    {
        var result = await CreateController().List(null, null, null, null, limit);

        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be("limit must be between 1 and 500.");
    }

    [Fact]
    public async Task Get_Existing_ReturnsRule()
    {
        var rule = TestEntities.AccountTypeRule(_accountTypeId);
        _tenant.Seed(rule);

        var result = await CreateController().Get(rule.Id, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<AccountTypeRuleResponse>()
            .Which.RuleName.Should().Be(rule.RuleName);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        var result = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
