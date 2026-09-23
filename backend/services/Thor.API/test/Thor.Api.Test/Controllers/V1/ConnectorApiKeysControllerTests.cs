using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Api.Constants;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.Api.Test.TestFixtures;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.Api.Test.Controllers.V1;

public class ConnectorApiKeysControllerTests
{
    private const string ActorId = "actor-1";

    private readonly string _masterDbName = $"master-{Guid.NewGuid()}";

    private static readonly MasterConnectionInfo ConnectionInfo = new("localhost", "unused", "unused", "unused");

    private MasterDbContext CreateMasterDbContext() =>
        new(new DbContextOptionsBuilder<MasterDbContext>().UseInMemoryDatabase(_masterDbName).Options);

    private Guid SeedTenant()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateMasterDbContext();
        db.Tenants.Add(new Tenant
        {
            TenantId = tenantId,
            DisplayName = "tenant-1",
            Subdomain = $"tenant-{tenantId:N}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return tenantId;
    }

    private void SeedScope(string scopeText)
    {
        using var db = CreateMasterDbContext();
        db.ApiScopes.Add(new ApiScope { ScopeText = scopeText });
        db.SaveChanges();
    }

    private ConnectorApiKeysController CreateController()
    {
        var factory = new InMemoryMasterDbContextFactory(_masterDbName);
        var service = new ConnectorApiKeyService(
            factory, ConnectionInfo, new ConnectorSecurityOptions("unused", "unit-test-pepper"), NullLogger<ConnectorApiKeyService>.Instance);

        return new ConnectorApiKeysController(service, NullLogger<ConnectorApiKeysController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    // Sets the trusted tenant/actor headers the controller reads directly (see the comment on
    // ConnectorApiKeysController.Create).
    private static void SetHeaders(
        ConnectorApiKeysController controller,
        bool includeTenantHeader = true,
        string? tenantHeaderValue = null,
        bool includeActorHeader = true,
        string? actorId = ActorId)
    {
        if (includeTenantHeader)
        {
            controller.Request.Headers[TenantConstants.TenantHeaderName] = tenantHeaderValue ?? Guid.NewGuid().ToString();
        }

        if (includeActorHeader && actorId is not null)
        {
            controller.Request.Headers[TenantConstants.ActorHeaderName] = actorId;
        }
    }

    private static CreateConnectorApiKeyRequest ValidRequest() => new("scanner", "My key", 0);

    [Fact]
    public async Task Create_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, includeTenantHeader: false);

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, tenantHeaderValue: "not-a-guid");

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_MissingActorHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, includeActorHeader: false);

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingScope_ReturnsBadRequest(string? scope)
    {
        var controller = CreateController();
        SetHeaders(controller);
        var request = ValidRequest() with { Scope = scope! };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Scope is required.");
    }

    [Fact]
    public async Task Create_UnknownTenant_ReturnsForbidden()
    {
        var controller = CreateController();
        SetHeaders(controller); // random, unseeded tenant id

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Create_UnknownScope_ReturnsBadRequest()
    {
        var tenantId = SeedTenant(); // deliberately no matching ApiScope seeded
        var controller = CreateController();
        SetHeaders(controller, tenantHeaderValue: tenantId.ToString());

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Scope 'scanner' was not found.");
    }

    [Fact]
    public async Task Create_ValidRequest_PersistsApiKeyAndReturnsCreated()
    {
        var tenantId = SeedTenant();
        SeedScope("scanner");
        var controller = CreateController();
        SetHeaders(controller, tenantHeaderValue: tenantId.ToString());

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var response = objectResult.Value.Should().BeOfType<ConnectorApiKeyResponse>().Subject;
        response.KeyId.Should().NotBeEmpty();
        response.RawApiKey.Should().NotBeNullOrWhiteSpace();

        using var verifyDb = CreateMasterDbContext();
        var persisted = verifyDb.TenantApiKeys.Single(k => k.KeyId == response.KeyId);
        persisted.TenantId.Should().Be(tenantId);
        persisted.Label.Should().Be("My key");
    }
}
