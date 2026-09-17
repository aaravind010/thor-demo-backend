using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Graph.BulkLoad;
using Thor.Workflows.Ingestion.Steps;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Steps;

/// <summary>
/// Exercises <see cref="GraphLoadPollStep"/> against a real Postgres (Testcontainers), with a
/// fake <see cref="INeptuneBulkLoaderClient"/> standing in for Neptune's bulk loader endpoint.
/// </summary>
public sealed class GraphLoadPollStepTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _context = NewContext();
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private TenantDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new TenantDbContext(options);
    }

    private async Task<Guid> SeedPendingJobAsync(Guid scanId, string loadId)
    {
        var job = new GraphBulkLoadJob
        {
            Id = Guid.NewGuid(), ScanId = scanId, LoadId = loadId, S3Uri = "s3://bucket/prefix/",
            Status = "started", StartedAt = DateTimeOffset.UtcNow,
        };
        _context.GraphBulkLoadJobs.Add(job);
        await _context.SaveChangesAsync();
        return job.Id;
    }

    private sealed class FakeBulkLoaderClient(params BulkLoadStatusResult[] statusesInOrder) : INeptuneBulkLoaderClient
    {
        private int _callCount;

        public Task<BulkLoadStartResult> StartLoadAsync(Uri s3SourceUri, string iamRoleArn, string region, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("GraphLoadPollStep never starts a load.");

        public Task<BulkLoadStatusResult> GetLoadStatusAsync(string loadId, CancellationToken cancellationToken = default)
        {
            var index = Math.Min(_callCount, statusesInOrder.Length - 1);
            _callCount++;
            return Task.FromResult(statusesInOrder[index]);
        }
    }

    private static BulkLoadStatusResult Completed(string loadId, long totalRecords = 5) =>
        new(loadId, BulkLoadStatus.Completed, "LOAD_COMPLETED", totalRecords, 0, 0, 0, []);

    private static BulkLoadStatusResult InProgress(string loadId) =>
        new(loadId, BulkLoadStatus.InProgress, "LOAD_IN_PROGRESS", 0, 0, 0, 0, []);

    private static BulkLoadStatusResult Failed(string loadId, string message) =>
        new(loadId, BulkLoadStatus.Failed, "LOAD_FAILED", 0, 0, 0, 0, [message]);

    private static BulkLoadStatusResult CompletedWithRowErrors(string loadId) =>
        new(loadId, BulkLoadStatus.Completed, "LOAD_COMPLETED", 5, 2, 0, 0, []);

    private sealed class FakeTenantConnectionManager(Func<Guid, TenantDbContext> factory) : ITenantConnectionManager
    {
        public Task<Npgsql.NpgsqlConnection> GetValidatedConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<TenantDbContext> GetTenantDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(factory(tenantId));
    }

    private static GraphLoadPollStep BuildStep(FakeBulkLoaderClient client, TimeSpan? pollInterval = null, TimeSpan? maxWait = null) =>
        new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => throw new NotSupportedException()),
            client, pollInterval ?? TimeSpan.FromMilliseconds(5), maxWait ?? TimeSpan.FromSeconds(5));

    [Fact]
    public async Task RunAsync_NoJobForScan_ReturnsNoPendingJob_WithoutCallingBulkLoader()
    {
        var step = BuildStep(new FakeBulkLoaderClient());

        var result = await step.RunAsync(_context, Guid.NewGuid());

        Assert.Equal("no-pending-job", result.Outcome);
        Assert.Null(result.LoadId);
    }

    [Fact]
    public async Task RunAsync_CompletesOnFirstPoll_MarksJobCompleted()
    {
        var scanId = Guid.NewGuid();
        await SeedPendingJobAsync(scanId, "load-1");
        var step = BuildStep(new FakeBulkLoaderClient(Completed("load-1")));

        var result = await step.RunAsync(_context, scanId);

        Assert.Equal("completed", result.Outcome);
        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("completed", job.Status);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task RunAsync_InProgressThenCompleted_PollsUntilTerminal()
    {
        var scanId = Guid.NewGuid();
        await SeedPendingJobAsync(scanId, "load-2");
        var step = BuildStep(new FakeBulkLoaderClient(InProgress("load-2"), InProgress("load-2"), Completed("load-2")));

        var result = await step.RunAsync(_context, scanId);

        Assert.Equal("completed", result.Outcome);
    }

    [Fact]
    public async Task RunAsync_Failed_MarksJobFailedAndThrows()
    {
        var scanId = Guid.NewGuid();
        await SeedPendingJobAsync(scanId, "load-3");
        var step = BuildStep(new FakeBulkLoaderClient(Failed("load-3", "s3 access denied")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(_context, scanId));

        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("failed", job.Status);
        Assert.Contains("s3 access denied", job.ErrorSummary);
    }

    [Fact]
    public async Task RunAsync_CompletedWithRowErrors_MarksJobFailedAndThrows()
    {
        var scanId = Guid.NewGuid();
        await SeedPendingJobAsync(scanId, "load-4");
        var step = BuildStep(new FakeBulkLoaderClient(CompletedWithRowErrors("load-4")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(_context, scanId));

        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("failed", job.Status);
    }

    [Fact]
    public async Task RunAsync_NeverReachesTerminalState_ThrowsTimeoutException()
    {
        var scanId = Guid.NewGuid();
        await SeedPendingJobAsync(scanId, "load-5");
        var step = BuildStep(
            new FakeBulkLoaderClient(InProgress("load-5")),
            pollInterval: TimeSpan.FromMilliseconds(5), maxWait: TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAsync<TimeoutException>(() => step.RunAsync(_context, scanId));

        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("started", job.Status);
    }
}
