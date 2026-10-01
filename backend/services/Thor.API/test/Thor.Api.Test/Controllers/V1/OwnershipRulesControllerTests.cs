using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.Support;
using Thor.DataConnectionManager;
using Thor.Rules.Ownership;

namespace Thor.Api.Test.Controllers.V1;

public class OwnershipRulesControllerTests
{
    private const string ValidDefinition = """{"schemaVersion":1,"field":"email","how":"exact"}""";

    private readonly InMemoryTenant _tenant = new();

    private OwnershipRulesController CreateController(
        ITenantConnectionManager? manager = null, bool includeTenantHeader = true, string? tenantHeaderValue = null) =>
        InMemoryTenant.WithTenantHeader(
            new OwnershipRulesController(new OwnershipRuleService(manager ?? _tenant.ConnectionManager())),
            includeTenantHeader, tenantHeaderValue);

    private static CreateOwnershipRuleRequest ValidRequest() => new(
        RuleName: "Custom email match", RuleType: "field_match", AppliesTo: "account",
        RuleDefinition: ValidDefinition, PrecisionScore: 0.8m);

    [Fact]
    public async Task Create_MissingTenantHeader_ReturnsBadRequest()
    {
        var result = await CreateController(includeTenantHeader: false).Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingRuleType_ReturnsBadRequest(string? ruleType)
    {
        var result = await CreateController().Create(ValidRequest() with { RuleType = ruleType! }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().Be("RuleType is required.");
    }

    [Fact]
    public async Task Create_PrecisionScoreZero_ReturnsBadRequest()
    {
        var result = await CreateController().Create(ValidRequest() with { PrecisionScore = 0 }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("PrecisionScore must be greater than 0 and at most 1.");
    }

    [Fact]
    public async Task Create_UnknownRuleType_ReturnsBadRequest()
    {
        var result = await CreateController().Create(ValidRequest() with { RuleType = "edge_hop_inheritance" }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<string>().Which.Should().StartWith("ruleType must be one of");
    }

    /// <summary>The entity-type string for groups is "grp", not "group".</summary>
    [Fact]
    public async Task Create_UnknownAppliesTo_ReturnsBadRequest()
    {
        var result = await CreateController().Create(ValidRequest() with { AppliesTo = "group" }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<string>().Which.Should().StartWith("appliesTo must be one of");
    }

    /// <summary>The same validator the Ownership workflow loads rules through rejects the definition, and its reason is surfaced.</summary>
    [Theory]
    [InlineData("field_match", "account", """{"schemaVersion":1,"field":"password","how":"exact"}""", "unrecognized field 'password'")]
    [InlineData("field_match", "account", """{"schemaVersion":2,"field":"email"}""", "schemaVersion")]
    [InlineData("majority_owner", "grp", """{"schemaVersion":1}""", "requires 'via'")]
    [InlineData("inherit_owner", "asset", """{"via":{"relType":"HAS_ACCESS","direction":"in","fromTypes":["account","grp"]},"prefer":{"prop":"is_admin","value":"yes"}}""", "prefer.value must be a boolean")]
    [InlineData("field_match", "account", "{not json", "malformed rule_definition")]
    public async Task Create_InvalidDefinition_ReturnsBadRequestWithValidatorReason(
        string ruleType, string appliesTo, string definition, string expectedReason)
    {
        var request = ValidRequest() with { RuleType = ruleType, AppliesTo = appliesTo, RuleDefinition = definition };

        var result = await CreateController().Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<string>().Which.Should().Contain(expectedReason);
        using var db = _tenant.CreateDbContext();
        db.OwnershipRules.Should().BeEmpty();
    }

    /// <summary>Taking a built-in default's name would stop the seeder from ever inserting that default.</summary>
    [Fact]
    public async Task Create_NameReservedByDefaultRule_ReturnsConflict()
    {
        var request = ValidRequest() with { RuleName = OwnershipRuleDefaults.EmailExactMatch };

        var result = await CreateController().Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        _tenant.Seed(TestEntities.OwnershipRule(ValidRequest().RuleName));

        var result = await CreateController().Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
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
        var result = await CreateController().Create(ValidRequest() with { IsActive = false }, CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var response = objectResult.Value.Should().BeOfType<OwnershipRuleResponse>().Subject;

        using var db = _tenant.CreateDbContext();
        var persisted = db.OwnershipRules.Single(r => r.Id == response.Id);
        persisted.RuleType.Should().Be("field_match");
        persisted.AppliesTo.Should().Be("account");
        persisted.RuleDefinition.Should().Be(ValidDefinition);
        persisted.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task List_FiltersByAppliesTo()
    {
        var accountRule = TestEntities.OwnershipRule("a");
        var groupRule = TestEntities.OwnershipRule("b");
        groupRule.AppliesTo = "grp";
        _tenant.Seed(accountRule, groupRule);

        var result = await CreateController().List(ruleType: null, appliesTo: "grp", isActive: null, after: null);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CursorPage<OwnershipRuleResponse>>()
            .Which.Items.Select(r => r.Id).Should().Equal(groupRule.Id);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        var result = await CreateController().Get(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }
}
