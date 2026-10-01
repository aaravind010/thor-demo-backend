using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.TaskApi.Constants;
using Thor.TaskApi.Controllers.V1;
using Thor.TaskApi.Models;
using Thor.TaskApi.Services;

namespace Thor.TaskApi.Test.Controllers;

// TaskService.ClaimTasksAsync, HeartbeatAsync, and UpdateStatusAsync all go through
// ScanTaskRepository methods that use ExecuteUpdateAsync — EF Core's InMemory provider doesn't
// support ExecuteUpdate/ExecuteDelete at all (confirmed directly: it throws
// InvalidOperationException), so only the header/body-validation path (which never reaches the
// service) can be exercised here for any of them. The claim logic was verified manually against
// a real local Postgres instance: a
// single claim returns the oldest Pending task first and flips it to InProgress, and under
// genuine concurrency (two overlapping UPDATE ... WHERE id = ANY(...) AND status = 'Pending'
// statements racing on the same rows) exactly one of them claims each row — the other affects 0
// rows for it — so no task is ever delivered to two callers.
public class TasksControllerTests
{
    private static readonly JsonElement EmptyAttributes = JsonDocument.Parse("{}").RootElement;

    private static TasksController CreateController()
    {
        var manager = Substitute.For<ITenantConnectionManager>();
        var masterFactory = Substitute.For<IMasterDbContextFactory>();
        var masterConnectionInfo = new MasterConnectionInfo("host", "db", "user", "password");
        var secretReader = Substitute.For<IAuthenticationSecretReader>();
        var service = new TaskService(
            manager, new ScanTaskReclaimOptions(TimeSpan.FromMinutes(30), MaxRetries: 10), masterFactory, masterConnectionInfo, secretReader);

        return new TasksController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    /// <summary>A request with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task Get_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.Get(CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task Get_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = "not-a-guid";

        var result = await controller.Get(CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A heartbeat with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task Heartbeat_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.Heartbeat(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A heartbeat with a tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task Heartbeat_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = "not-a-guid";

        var result = await controller.Heartbeat(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A status update with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task UpdateStatus_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.UpdateStatus(Guid.NewGuid(), new UpdateTaskStatusRequest(Thor.TaskApi.Models.ScanTaskStatus.Completed), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A status update with a tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task UpdateStatus_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = "not-a-guid";

        var result = await controller.UpdateStatus(Guid.NewGuid(), new UpdateTaskStatusRequest(Thor.TaskApi.Models.ScanTaskStatus.Completed), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// Only connector-settable statuses (pending/completed/failed) are accepted; anything else —
    /// including the DB-layer <see cref="Thor.DataLayer.Models.Tenants.ScanTaskStatus"/> values,
    /// which are never valid on the wire — is rejected before the service is called.
    /// </summary>
    [Theory]
    [InlineData(Thor.DataLayer.Models.Tenants.ScanTaskStatus.Pending)]
    [InlineData(Thor.DataLayer.Models.Tenants.ScanTaskStatus.Completed)]
    [InlineData(Thor.DataLayer.Models.Tenants.ScanTaskStatus.InProgress)]
    [InlineData(Thor.DataLayer.Models.Tenants.ScanTaskStatus.Dead)]
    [InlineData("NotAStatus")]
    [InlineData("")]
    public async Task UpdateStatus_DisallowedStatus_ReturnsBadRequest(string status)
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = Guid.NewGuid().ToString();

        var result = await controller.UpdateStatus(Guid.NewGuid(), new UpdateTaskStatusRequest(status), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A settings request with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task GetSettings_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.GetSettings(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A settings request with a tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task GetSettings_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = "not-a-guid";

        var result = await controller.GetSettings(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A progress update with no tenant header at all is rejected before the service is called.</summary>
    [Fact]
    public async Task AddProgress_MissingTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.AddProgress(Guid.NewGuid(), new TaskProgressRequest(3000, 30, EmptyAttributes), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A progress update with a tenant header that isn't a parseable guid is rejected before the service is called.</summary>
    [Fact]
    public async Task AddProgress_InvalidTenantHeader_ReturnsBadRequest()
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = "not-a-guid";

        var result = await controller.AddProgress(Guid.NewGuid(), new TaskProgressRequest(3000, 30, EmptyAttributes), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A percentageComplete outside 0-100 is rejected before the service is called.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task AddProgress_PercentageCompleteOutOfRange_ReturnsBadRequest(int percentageComplete)
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = Guid.NewGuid().ToString();

        var result = await controller.AddProgress(Guid.NewGuid(), new TaskProgressRequest(3000, percentageComplete, EmptyAttributes), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>A negative timeElapsed is rejected before the service is called.</summary>
    [Fact]
    public async Task AddProgress_NegativeTimeElapsed_ReturnsBadRequest()
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = Guid.NewGuid().ToString();

        var result = await controller.AddProgress(Guid.NewGuid(), new TaskProgressRequest(-1, 30, EmptyAttributes), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>attributes must be a JSON object — a JSON array, string, etc. is rejected before the service is called.</summary>
    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"not-an-object\"")]
    [InlineData("42")]
    public async Task AddProgress_AttributesNotAnObject_ReturnsBadRequest(string attributesJson)
    {
        var controller = CreateController();
        controller.Request.Headers[UploadConstants.TenantHeaderName] = Guid.NewGuid().ToString();
        var attributes = JsonDocument.Parse(attributesJson).RootElement;

        var result = await controller.AddProgress(Guid.NewGuid(), new TaskProgressRequest(3000, 30, attributes), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// attributes fields aren't restricted to one value type — a mix of strings and numbers in
    /// the same object must bind and persist without a serialization error.
    /// </summary>
    [Fact]
    public async Task AddProgress_AttributesWithMixedValueTypes_ReturnsOk()
    {
        var taskId = Guid.NewGuid();
        var tenantDbName = $"tenant-{Guid.NewGuid()}";
        var manager = Substitute.For<ITenantConnectionManager>();
        manager.GetTenantDbContextAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new TenantDbContext(
                new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(tenantDbName).Options)));

        using (var seed = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>().UseInMemoryDatabase(tenantDbName).Options))
        {
            seed.Tasks.Add(new ScanTask { Id = taskId, ScanId = Guid.NewGuid(), SourceId = Guid.NewGuid(), Status = "InProgress", CreatedAt = DateTimeOffset.UtcNow });
            seed.SaveChanges();
        }

        var masterFactory = Substitute.For<IMasterDbContextFactory>();
        var masterConnectionInfo = new MasterConnectionInfo("host", "db", "user", "password");
        var service = new TaskService(
            manager, new ScanTaskReclaimOptions(TimeSpan.FromMinutes(30), MaxRetries: 10), masterFactory, masterConnectionInfo, Substitute.For<IAuthenticationSecretReader>());
        var controller = new TasksController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.Request.Headers[UploadConstants.TenantHeaderName] = Guid.NewGuid().ToString();
        var attributes = JsonDocument.Parse("""{"userCount":"100","something":"test","someInt":100}""").RootElement;

        var result = await controller.AddProgress(taskId, new TaskProgressRequest(3000, 30, attributes), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
    }
}
