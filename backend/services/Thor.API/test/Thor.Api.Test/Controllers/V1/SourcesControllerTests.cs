using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Thor.Api.Constants;
using Thor.Api.Controllers.V1;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager;
using Thor.DataConnectionManager.Exceptions;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;
using Thor.DataLayer.Models.Tenants;

namespace Thor.Api.Test.Controllers.V1;

public class SourcesControllerTests
{
    private const short ConnectorTypeId = 1;

    private readonly string _tenantDbName = $"tenant-{Guid.NewGuid()}";
    private readonly string _masterDbName = $"master-{Guid.NewGuid()}";

    // The in-memory provider doesn't support real transactions; SourceService wraps its
    // writes in one, so this warning is expected and safe to ignore for these tests.
    private TenantDbContext CreateTenantDbContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(_tenantDbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private MasterDbContext CreateMasterDbContext() =>
        new(new DbContextOptionsBuilder<MasterDbContext>().UseInMemoryDatabase(_masterDbName).Options);

    private void SeedMasterDb(Action<MasterDbContext> seed)
    {
        using var context = CreateMasterDbContext();
        seed(context);
        context.SaveChanges();
    }

    private void SeedConnectorType(short id, string name) =>
        SeedMasterDb(db => db.ConnectorTypes.Add(new ConnectorType { Id = id, Name = name }));

    private Guid SeedSource(short connectorType, string name)
    {
        var id = Guid.NewGuid();
        using var context = CreateTenantDbContext();
        context.Sources.Add(new Source
        {
            Id = id,
            ConnectorType = connectorType,
            Name = name,
            Config = "{}",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        return id;
    }

    private SourcesController CreateController(ITenantConnectionManager? tenantConnectionManager = null)
    {
        var manager = tenantConnectionManager ?? CreateDefaultTenantConnectionManager();

        var masterFactory = Substitute.For<IMasterDbContextFactory>();
        masterFactory.Create(Arg.Any<MasterConnectionInfo>()).Returns(_ => CreateMasterDbContext());

        var masterConnectionInfo = new MasterConnectionInfo("host", "db", "user", "password");
        var service = new SourceService(manager, masterFactory, masterConnectionInfo);

        return new SourcesController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private ITenantConnectionManager CreateDefaultTenantConnectionManager()
    {
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(CreateTenantDbContext()));
        return manager;
    }

    // Sets the trusted tenant header the controller reads directly (see the comment on
    // SourcesController.Create). Defaults to a fresh random guid string; pass a non-guid
    // string to exercise the "invalid header" path, or includeTenantHeader: false to omit it.
    private static void SetHeaders(
        SourcesController controller,
        bool includeTenantHeader = true,
        string? tenantHeaderValue = null)
    {
        if (includeTenantHeader)
        {
            controller.Request.Headers[TenantConstants.TenantHeaderName] = tenantHeaderValue ?? Guid.NewGuid().ToString();
        }
    }

    private static CreateSourceRequest ValidRequest() =>
        new(ConnectorType: ConnectorTypeId, Name: "source-1", Config: "{}");

    /// <summary>A request with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, includeTenantHeader: false);

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, tenantHeaderValue: "not-a-guid");

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A null, empty, or whitespace-only Name is rejected with the exact validation message.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingName_ReturnsBadRequest(string? name)
    {
        var controller = CreateController();
        SetHeaders(controller);
        var request = ValidRequest() with { Name = name! };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Name is required.");
    }

    /// <summary>A null, empty, or whitespace-only Config is rejected with the exact validation message.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingConfig_ReturnsBadRequest(string? config)
    {
        var controller = CreateController();
        SetHeaders(controller);
        var request = ValidRequest() with { Config = config! };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Config is required.");
    }

    /// <summary>A connector type that doesn't exist in the Master metadata DB is rejected.</summary>
    [Fact]
    public async Task Create_UnknownConnectorType_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// A tenant header that parses but has no routing (ITenantConnectionManager fails closed with
    /// TenantNotFoundException) surfaces as 403, not a validation error.
    /// </summary>
    [Fact]
    public async Task Create_TenantNotFound_ReturnsForbidden()
    {
        SeedConnectorType(ConnectorTypeId, "Active Directory");
        var tenantId = Guid.NewGuid();
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<TenantDbContext>(new TenantNotFoundException(tenantId)));
        var controller = CreateController(manager);
        SetHeaders(controller, tenantHeaderValue: tenantId.ToString());

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>A fully valid request persists a Source and returns 201 with the new row.</summary>
    [Fact]
    public async Task Create_ValidRequest_PersistsSourceAndReturnsCreated()
    {
        SeedConnectorType(ConnectorTypeId, "Active Directory");
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(ValidRequest(), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var response = objectResult.Value.Should().BeOfType<SourceResponse>().Subject;
        response.Id.Should().NotBeEmpty();
        response.Name.Should().Be("source-1");
        response.ConnectorType.Should().Be(ConnectorTypeId);
        response.IsActive.Should().BeTrue();

        using var verifyDb = CreateTenantDbContext();
        var persisted = verifyDb.Sources.Single(s => s.Id == response.Id);
        persisted.Name.Should().Be("source-1");
        persisted.Config.Should().Be("{}");
    }

    /// <summary>A request with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task List_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, includeTenantHeader: false);

        var result = await controller.List(CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// A tenant header that parses but has no routing (ITenantConnectionManager fails closed with
    /// TenantNotFoundException) surfaces as 403, not a validation error.
    /// </summary>
    [Fact]
    public async Task List_TenantNotFound_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<TenantDbContext>(new TenantNotFoundException(tenantId)));
        var controller = CreateController(manager);
        SetHeaders(controller, tenantHeaderValue: tenantId.ToString());

        var result = await controller.List(CancellationToken.None);

        result.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>Returns every Source persisted in the caller's tenant database.</summary>
    [Fact]
    public async Task List_ValidRequest_ReturnsAllSourcesForTenant()
    {
        SeedConnectorType(ConnectorTypeId, "Active Directory");
        var firstId = SeedSource(ConnectorTypeId, "source-1");
        var secondId = SeedSource(ConnectorTypeId, "source-2");
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.List(CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IReadOnlyList<SourceResponse>>().Subject;
        response.Select(s => s.Id).Should().BeEquivalentTo([firstId, secondId]);
    }

    /// <summary>An empty tenant database returns an empty list, not an error.</summary>
    [Fact]
    public async Task List_NoSources_ReturnsEmptyList()
    {
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.List(CancellationToken.None);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IReadOnlyList<SourceResponse>>().Subject;
        response.Should().BeEmpty();
    }
}
