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

public class ScanConfigsControllerTests
{
    private const string ActorId = "actor-1";
    private const short ConnectorTypeId = 1;

    private readonly string _tenantDbName = $"tenant-{Guid.NewGuid()}";
    private readonly string _masterDbName = $"master-{Guid.NewGuid()}";

    // The in-memory provider doesn't support real transactions; ScanConfigService wraps its
    // writes in one, so this warning is expected and safe to ignore for these tests.
    private TenantDbContext CreateTenantDbContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(_tenantDbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private MasterDbContext CreateMasterDbContext() =>
        new(new DbContextOptionsBuilder<MasterDbContext>().UseInMemoryDatabase(_masterDbName).Options);

    private void SeedTenantDb(Action<TenantDbContext> seed)
    {
        using var context = CreateTenantDbContext();
        seed(context);
        context.SaveChanges();
    }

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
        SeedTenantDb(db => db.Sources.Add(new Source
        {
            Id = id,
            ConnectorType = connectorType,
            Name = name,
            Config = "{}",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }));
        return id;
    }

    private Guid SeedAuthenticationType(short connectorType, string name)
    {
        var id = Guid.NewGuid();
        SeedMasterDb(db => db.AuthenticationTypes.Add(new AuthenticationType
        {
            Id = id,
            Name = name,
            ConnectorTypeId = connectorType,
        }));
        return id;
    }

    private Guid SeedAuthenticationMethod(Guid typeId, string name)
    {
        var id = Guid.NewGuid();
        SeedTenantDb(db => db.AuthenticationMethods.Add(new AuthenticationMethod
        {
            Id = id,
            TypeId = typeId,
            Name = name,
        }));
        return id;
    }

    // Seeds a source, an auth method whose type matches the source's connector type, and no
    // required config fields — the minimal set of rows CreateAsync needs to succeed.
    private (Guid SourceId, Guid AuthMethodId) SeedValidScanConfigPrerequisites()
    {
        SeedConnectorType(ConnectorTypeId, "Active Directory");
        var authTypeId = SeedAuthenticationType(ConnectorTypeId, "API Key");
        var sourceId = SeedSource(ConnectorTypeId, "source-1");
        var authMethodId = SeedAuthenticationMethod(authTypeId, "auth-method-1");

        return (sourceId, authMethodId);
    }

    private ScanConfigsController CreateController(ITenantConnectionManager? tenantConnectionManager = null)
    {
        var manager = tenantConnectionManager ?? CreateDefaultTenantConnectionManager();

        var masterFactory = Substitute.For<IMasterDbContextFactory>();
        masterFactory.Create(Arg.Any<MasterConnectionInfo>()).Returns(_ => CreateMasterDbContext());

        var masterConnectionInfo = new MasterConnectionInfo("host", "db", "user", "password");
        var service = new ScanConfigService(manager, masterFactory, masterConnectionInfo);

        return new ScanConfigsController(service)
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

    // Sets the trusted tenant/actor headers the controller reads directly (see the comment on
    // ScanConfigsController.Create). tenantHeaderValue defaults to a fresh random guid string;
    // pass a non-guid string to exercise the "invalid header" path, or includeTenantHeader/
    // includeActorHeader: false to omit a header entirely.
    private static void SetHeaders(
        ScanConfigsController controller,
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

    private static CreateScanConfigRequest ValidRequest(Guid sourceId, Guid authMethodId) =>
        new(
            Name: "scan-config-1",
            Description: null,
            SourceIds: [sourceId],
            AuthMethodId: authMethodId,
            ConfigValues: []);

    /// <summary>A request with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, includeTenantHeader: false);

        var result = await controller.Create(ValidRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, tenantHeaderValue: "not-a-guid");

        var result = await controller.Create(ValidRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A request with no actor header is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_MissingActorHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, includeActorHeader: false);

        var result = await controller.Create(ValidRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

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
        var request = ValidRequest(Guid.NewGuid(), Guid.NewGuid()) with { Name = name! };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Name is required.");
    }

    /// <summary>An empty SourceIds list is rejected with the exact validation message.</summary>
    [Fact]
    public async Task Create_EmptySourceIds_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller);
        var request = ValidRequest(Guid.NewGuid(), Guid.NewGuid()) with { SourceIds = [] };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("At least one source id is required.");
    }

    /// <summary>
    /// A tenant header that parses but has no routing (ITenantConnectionManager fails closed with
    /// TenantNotFoundException) surfaces as 403, not a validation error.
    /// </summary>
    [Fact]
    public async Task Create_TenantNotFound_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<TenantDbContext>(new TenantNotFoundException(tenantId)));
        var controller = CreateController(manager);
        SetHeaders(controller, tenantHeaderValue: tenantId.ToString());

        var result = await controller.Create(ValidRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// A fully valid request persists a ScanConfig with its source mapping and returns 201 with
    /// the new id.
    /// </summary>
    [Fact]
    public async Task Create_ValidRequest_PersistsScanConfigAndReturnsCreated()
    {
        var (sourceId, authMethodId) = SeedValidScanConfigPrerequisites();
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(ValidRequest(sourceId, authMethodId), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var response = objectResult.Value.Should().BeOfType<ScanConfigResponse>().Subject;
        response.Id.Should().NotBeEmpty();

        using var verifyDb = CreateTenantDbContext();
        var persisted = verifyDb.ScanConfigs
            .Include(c => c.SourceMappings)
            .Single(c => c.Id == response.Id);
        persisted.Name.Should().Be("scan-config-1");
        persisted.AuthMethodId.Should().Be(authMethodId);
        persisted.SourceMappings.Should().ContainSingle(m => m.SourceId == sourceId);
    }

    /// <summary>A source id that doesn't exist in the tenant database is rejected.</summary>
    [Fact]
    public async Task Create_UnknownSourceId_ReturnsBadRequest()
    {
        var (_, authMethodId) = SeedValidScanConfigPrerequisites();
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(ValidRequest(Guid.NewGuid(), authMethodId), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>Sources that don't all share one connector type are rejected.</summary>
    [Fact]
    public async Task Create_SourcesWithMixedConnectorTypes_ReturnsBadRequest()
    {
        var (sourceId, authMethodId) = SeedValidScanConfigPrerequisites();
        SeedConnectorType(ConnectorTypeId + 1, "Other Connector");
        var otherSourceId = SeedSource(ConnectorTypeId + 1, "source-2");
        var controller = CreateController();
        SetHeaders(controller);
        var request = ValidRequest(sourceId, authMethodId) with { SourceIds = [sourceId, otherSourceId] };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>An auth method id that doesn't exist in the tenant database is rejected.</summary>
    [Fact]
    public async Task Create_UnknownAuthMethod_ReturnsBadRequest()
    {
        var (sourceId, _) = SeedValidScanConfigPrerequisites();
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(ValidRequest(sourceId, Guid.NewGuid()), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>An auth method whose type belongs to a different connector type than the sources is rejected.</summary>
    [Fact]
    public async Task Create_AuthMethodConnectorTypeMismatch_ReturnsBadRequest()
    {
        var (sourceId, _) = SeedValidScanConfigPrerequisites();
        SeedConnectorType(ConnectorTypeId + 1, "Other Connector");
        var mismatchedAuthTypeId = SeedAuthenticationType(ConnectorTypeId + 1, "Other Auth Type");
        var mismatchedAuthMethodId = SeedAuthenticationMethod(mismatchedAuthTypeId, "auth-method-2");
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(ValidRequest(sourceId, mismatchedAuthMethodId), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>Omitting a value for a required connector config field is rejected.</summary>
    [Fact]
    public async Task Create_MissingRequiredConfigValue_ReturnsBadRequest()
    {
        var (sourceId, authMethodId) = SeedValidScanConfigPrerequisites();
        SeedMasterDb(db => db.ConnectorConfigFields.Add(new ConnectorConfigField
        {
            Id = Guid.NewGuid(),
            ConnectorTypeId = ConnectorTypeId,
            FieldName = "base_url",
            DisplayName = "Base URL",
            InputType = "text",
            Required = true,
        }));
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(ValidRequest(sourceId, authMethodId), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A config value for a field id the connector type doesn't define is rejected.</summary>
    [Fact]
    public async Task Create_UnknownConfigValue_ReturnsBadRequest()
    {
        var (sourceId, authMethodId) = SeedValidScanConfigPrerequisites();
        var controller = CreateController();
        SetHeaders(controller);
        var request = ValidRequest(sourceId, authMethodId) with
        {
            ConfigValues = [new ScanConnectorConfigValueRequest(Guid.NewGuid(), "value")],
        };

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
