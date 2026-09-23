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
using Thor.DataLayer.Models.Tenants;

namespace Thor.Api.Test.Controllers.V1;

// Deliberately self-contained (own DbContext/controller/header helpers, not shared with
// ScanConfigsControllerTests) so this file can land independently of whatever else is in
// flight against the shared Thor.Api.Test project without merge conflicts.
public class ScanControllerTests
{
    private const short ConnectorTypeId = 1;

    private readonly string _tenantDbName = $"tenant-{Guid.NewGuid()}";

    // The in-memory provider doesn't support real transactions; ScanService wraps its writes
    // in one, so this warning is expected and safe to ignore for these tests.
    private TenantDbContext CreateTenantDbContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(_tenantDbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private void SeedTenantDb(Action<TenantDbContext> seed)
    {
        using var context = CreateTenantDbContext();
        seed(context);
        context.SaveChanges();
    }

    // Seeds a ScanConfig with one source mapping per given source id (each backed by a real
    // Source row) — the minimal set of rows ScanService.CreateAsync needs to produce one
    // ScanTask per source.
    private Guid SeedScanConfig(params Guid[] sourceIds)
    {
        var authMethodId = Guid.NewGuid();
        var scanConfigId = Guid.NewGuid();

        SeedTenantDb(db =>
        {
            db.AuthenticationMethods.Add(new AuthenticationMethod
            {
                Id = authMethodId,
                TypeId = Guid.NewGuid(),
                Name = "auth-method-1",
            });

            var scanConfig = new ScanConfig
            {
                Id = scanConfigId,
                Name = "scan-config-1",
                AuthMethodId = authMethodId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "actor-1",
                UpdatedBy = "actor-1",
            };

            foreach (var sourceId in sourceIds)
            {
                db.Sources.Add(new Source
                {
                    Id = sourceId,
                    ConnectorType = ConnectorTypeId,
                    Name = $"source-{sourceId}",
                    Config = "{}",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
                scanConfig.SourceMappings.Add(new ScanSourceMapping
                {
                    Id = Guid.NewGuid(),
                    ScanConfigId = scanConfigId,
                    SourceId = sourceId,
                });
            }

            db.ScanConfigs.Add(scanConfig);
        });

        return scanConfigId;
    }

    // A source mapping whose Source has since been unlinked (SourceId left null) — ScanService
    // is expected to skip it rather than produce a task with no source.
    private Guid SeedScanConfigWithUnlinkedSourceMapping()
    {
        var authMethodId = Guid.NewGuid();
        var scanConfigId = Guid.NewGuid();

        SeedTenantDb(db =>
        {
            db.AuthenticationMethods.Add(new AuthenticationMethod
            {
                Id = authMethodId,
                TypeId = Guid.NewGuid(),
                Name = "auth-method-1",
            });

            var scanConfig = new ScanConfig
            {
                Id = scanConfigId,
                Name = "scan-config-1",
                AuthMethodId = authMethodId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "actor-1",
                UpdatedBy = "actor-1",
            };
            scanConfig.SourceMappings.Add(new ScanSourceMapping
            {
                Id = Guid.NewGuid(),
                ScanConfigId = scanConfigId,
                SourceId = null,
            });

            db.ScanConfigs.Add(scanConfig);
        });

        return scanConfigId;
    }

    private ScanController CreateController(ITenantConnectionManager? tenantConnectionManager = null)
    {
        var manager = tenantConnectionManager ?? CreateDefaultTenantConnectionManager();
        var service = new ScanService(manager);

        return new ScanController(service)
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
    // ScanController.Create — unlike ScanConfigsController, no actor header is required here).
    // tenantHeaderValue defaults to a fresh random guid string; pass a non-guid string to
    // exercise the "invalid header" path, or includeTenantHeader: false to omit it entirely.
    private static void SetHeaders(
        ScanController controller,
        bool includeTenantHeader = true,
        string? tenantHeaderValue = null)
    {
        if (includeTenantHeader)
        {
            controller.Request.Headers[TenantConstants.TenantHeaderName] = tenantHeaderValue ?? Guid.NewGuid().ToString();
        }
    }

    /// <summary>A request with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, includeTenantHeader: false);

        var result = await controller.Create(new CreateScanRequest(Guid.NewGuid()), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task Create_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller, tenantHeaderValue: "not-a-guid");

        var result = await controller.Create(new CreateScanRequest(Guid.NewGuid()), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>An empty ScanConfigId is rejected with the exact validation message.</summary>
    [Fact]
    public async Task Create_EmptyScanConfigId_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(new CreateScanRequest(Guid.Empty), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("ScanConfigId is required.");
    }

    /// <summary>A ScanType outside 'scheduled'/'instant' is rejected with the exact validation message.</summary>
    [Fact]
    public async Task Create_InvalidScanType_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(
            new CreateScanRequest(Guid.NewGuid(), "invalid"), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("ScanType must be 'scheduled' or 'instant'.");
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

        var result = await controller.Create(new CreateScanRequest(Guid.NewGuid()), CancellationToken.None);

        result.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>A ScanConfigId that doesn't exist in the tenant database is rejected.</summary>
    [Fact]
    public async Task Create_UnknownScanConfig_ReturnsBadRequest()
    {
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(new CreateScanRequest(Guid.NewGuid()), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// A valid ScanConfigId persists a Scan with one ScanTask per mapped source and returns 201
    /// with a Pending scan and matching task list. ScanType defaults to 'instant' when omitted.
    /// </summary>
    [Fact]
    public async Task Create_ValidRequest_PersistsScanAndReturnsCreated()
    {
        var sourceId = Guid.NewGuid();
        var scanConfigId = SeedScanConfig(sourceId);
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(new CreateScanRequest(scanConfigId), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var response = objectResult.Value.Should().BeOfType<ScanResponse>().Subject;
        response.ScanConfigId.Should().Be(scanConfigId);
        response.Status.Should().Be(ScanStatus.Pending);
        response.Tasks.Should().ContainSingle(t => t.SourceId == sourceId && t.Status == ScanTaskStatus.Pending);

        using var verifyDb = CreateTenantDbContext();
        var persisted = verifyDb.Scans.Include(s => s.Tasks).Single(s => s.Id == response.ScanId);
        persisted.ScanConfigId.Should().Be(scanConfigId);
        persisted.ScanType.Should().Be(ScanTriggerType.Instant);
        persisted.TotalTasks.Should().Be(1);
        persisted.CompletedTasks.Should().Be(0);
        persisted.Tasks.Should().ContainSingle(t => t.SourceId == sourceId);
    }

    /// <summary>An explicit ScanType of 'scheduled' is persisted as given, not defaulted.</summary>
    [Fact]
    public async Task Create_ScheduledScanType_PersistsScheduledScanType()
    {
        var sourceId = Guid.NewGuid();
        var scanConfigId = SeedScanConfig(sourceId);
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(
            new CreateScanRequest(scanConfigId, ScanTriggerType.Scheduled), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        var response = objectResult.Value.Should().BeOfType<ScanResponse>().Subject;

        using var verifyDb = CreateTenantDbContext();
        var persisted = verifyDb.Scans.Single(s => s.Id == response.ScanId);
        persisted.ScanType.Should().Be(ScanTriggerType.Scheduled);
    }

    /// <summary>A ScanConfig with more than one mapped source produces one task per source.</summary>
    [Fact]
    public async Task Create_ScanConfigWithMultipleSources_CreatesOneTaskPerSource()
    {
        var sourceIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var scanConfigId = SeedScanConfig(sourceIds);
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(new CreateScanRequest(scanConfigId), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        var response = objectResult.Value.Should().BeOfType<ScanResponse>().Subject;
        response.Tasks.Should().HaveCount(3);
        response.Tasks.Select(t => t.SourceId).Should().BeEquivalentTo(sourceIds);
    }

    /// <summary>A source mapping whose Source has been unlinked (null) doesn't produce a task.</summary>
    [Fact]
    public async Task Create_ScanConfigWithUnlinkedSourceMapping_SkipsThatMapping()
    {
        var scanConfigId = SeedScanConfigWithUnlinkedSourceMapping();
        var controller = CreateController();
        SetHeaders(controller);

        var result = await controller.Create(new CreateScanRequest(scanConfigId), CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        var response = objectResult.Value.Should().BeOfType<ScanResponse>().Subject;
        response.Tasks.Should().BeEmpty();
    }
}
