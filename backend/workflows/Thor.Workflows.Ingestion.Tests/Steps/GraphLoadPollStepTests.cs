using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Graph;
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

    private async Task<Guid> SeedPendingJobAsync(
        Guid scanId, Guid scanManifestId, string loadId, string? pendingVertexDeletes = null, string? pendingEdgeDeletes = null)
    {
        var job = new GraphBulkLoadJob
        {
            Id = Guid.NewGuid(), ScanId = scanId, ScanManifestId = scanManifestId, LoadId = loadId, S3Uri = "s3://bucket/prefix/",
            Status = "started", StartedAt = DateTimeOffset.UtcNow,
            PendingVertexDeletes = pendingVertexDeletes, PendingEdgeDeletes = pendingEdgeDeletes,
        };
        _context.GraphBulkLoadJobs.Add(job);
        await _context.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>`workflow.scan_id`/`scan_manifest_id` are real foreign keys, so every test needs a real Scan/ScanManifest pair, not arbitrary Guids.</summary>
    private async Task<Guid> CreateScanAsync()
    {
        var authMethod = new AuthenticationMethod { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Name = "test-auth" };
        var scanConfig = new ScanConfig
        {
            Id = Guid.NewGuid(), Name = "test-scan-config", AuthMethodId = authMethod.Id,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = "test", UpdatedBy = "test",
        };
        var scan = new Scan { Id = Guid.NewGuid(), ScanConfigId = scanConfig.Id, ScanType = "initial", Status = "running" };
        _context.AuthenticationMethods.Add(authMethod);
        _context.ScanConfigs.Add(scanConfig);
        _context.Scans.Add(scan);
        await _context.SaveChangesAsync();
        return scan.Id;
    }

    private async Task<Guid> CreateManifestAsync(Guid scanId)
    {
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(), ScanId = scanId, FileLocations = [], Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _context.ScanManifests.Add(manifest);
        await _context.SaveChangesAsync();
        return manifest.Id;
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

    private sealed class FakeGraphVertexStore : IGraphVertexStore
    {
        private readonly object _lock = new();
        public List<(Guid TenantId, string EntityType, Guid Id)> Deleted { get; } = [];
        public Task UpsertVertexAsync(Guid tenantId, string entityType, Guid id, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertVerticesAsync(Guid tenantId, string entityType, IReadOnlyList<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> vertices, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteVertexAsync(Guid tenantId, string entityType, Guid id, CancellationToken cancellationToken = default)
        {
            lock (_lock) { Deleted.Add((tenantId, entityType, id)); }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGraphEdgeStore : IGraphEdgeStore
    {
        private readonly object _lock = new();
        public List<(Guid TenantId, string RelType, GraphVertexRef From, GraphVertexRef To)> Deleted { get; } = [];
        public Task UpsertEdgeAsync(Guid tenantId, string relType, GraphVertexRef from, GraphVertexRef to, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteEdgeAsync(Guid tenantId, string relType, GraphVertexRef from, GraphVertexRef to, CancellationToken cancellationToken = default)
        {
            lock (_lock) { Deleted.Add((tenantId, relType, from, to)); }
            return Task.CompletedTask;
        }
    }

    private static GraphLoadPollStep BuildStep(
        FakeBulkLoaderClient client, FakeGraphVertexStore? vertexStore = null, FakeGraphEdgeStore? edgeStore = null) =>
        new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => throw new NotSupportedException()),
            vertexStore ?? new FakeGraphVertexStore(), edgeStore ?? new FakeGraphEdgeStore(), client);

    [Fact]
    public async Task RunAsync_NoJobForScan_ReturnsNoPendingJob_WithoutCallingBulkLoader()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        var step = BuildStep(new FakeBulkLoaderClient());

        var result = await step.RunAsync(_context, Guid.NewGuid(), scanId, scanManifestId);

        Assert.Equal("no-pending-job", result.Outcome);
        Assert.Null(result.LoadId);

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("completed", workflow.Status);
        Assert.Equal(result.WorkflowId, workflow.Id);
    }

    [Fact]
    public async Task RunAsync_JobExistsForDifferentManifest_ReturnsNoPendingJob()
    {
        var scanId = await CreateScanAsync();
        var otherManifestId = await CreateManifestAsync(scanId);
        var scanManifestId = await CreateManifestAsync(scanId);
        await SeedPendingJobAsync(scanId, otherManifestId, "load-other");
        var step = BuildStep(new FakeBulkLoaderClient());

        var result = await step.RunAsync(_context, Guid.NewGuid(), scanId, scanManifestId);

        Assert.Equal("no-pending-job", result.Outcome);
        Assert.Null(result.LoadId);

        var otherJob = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanManifestId == otherManifestId);
        Assert.Equal("started", otherJob.Status);
    }

    [Fact]
    public async Task RunAsync_CompletesOnFirstPoll_MarksJobCompleted()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await SeedPendingJobAsync(scanId, scanManifestId, "load-1");
        var step = BuildStep(new FakeBulkLoaderClient(Completed("load-1")));

        var result = await step.RunAsync(_context, Guid.NewGuid(), scanId, scanManifestId);

        Assert.Equal("completed", result.Outcome);
        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("completed", job.Status);
        Assert.NotNull(job.CompletedAt);

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("completed", workflow.Status);
    }

    [Fact]
    public async Task RunAsync_CompletesWithPendingDeletes_AppliesDeletesAndClearsSnapshot()
    {
        var tenantId = Guid.NewGuid();
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        var accountId = Guid.NewGuid();
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();
        var vertexDeletesJson = JsonSerializer.Serialize(new[] { new PendingVertexDelete("account", accountId) });
        var edgeDeletesJson = JsonSerializer.Serialize(new[] { new PendingEdgeDelete("memberOf", "account", fromId, "grp", toId) });
        await SeedPendingJobAsync(scanId, scanManifestId, "load-1", vertexDeletesJson, edgeDeletesJson);
        var vertexStore = new FakeGraphVertexStore();
        var edgeStore = new FakeGraphEdgeStore();
        var step = BuildStep(new FakeBulkLoaderClient(Completed("load-1")), vertexStore, edgeStore);

        var result = await step.RunAsync(_context, tenantId, scanId, scanManifestId);

        Assert.Equal(1, result.VerticesDeleted);
        Assert.Equal(1, result.EdgesDeleted);
        Assert.Equal((tenantId, "account", accountId), vertexStore.Deleted.Single());
        Assert.Equal((tenantId, "memberOf", new GraphVertexRef("account", fromId), new GraphVertexRef("grp", toId)), edgeStore.Deleted.Single());

        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Null(job.PendingVertexDeletes);
        Assert.Null(job.PendingEdgeDeletes);
    }

    [Fact]
    public async Task RunAsync_Failed_DoesNotApplyPendingDeletes()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        var vertexDeletesJson = JsonSerializer.Serialize(new[] { new PendingVertexDelete("account", Guid.NewGuid()) });
        await SeedPendingJobAsync(scanId, scanManifestId, "load-3b", vertexDeletesJson);
        var vertexStore = new FakeGraphVertexStore();
        var step = BuildStep(new FakeBulkLoaderClient(Failed("load-3b", "s3 access denied")), vertexStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(_context, Guid.NewGuid(), scanId, scanManifestId));

        Assert.Empty(vertexStore.Deleted);
        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("failed", job.Status);
        Assert.NotNull(job.PendingVertexDeletes);
    }

    [Fact]
    public async Task RunAsync_StillInProgress_ReturnsInProgressOutcome_WithoutMarkingJobOrWorkflowTerminal()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await SeedPendingJobAsync(scanId, scanManifestId, "load-2");
        var step = BuildStep(new FakeBulkLoaderClient(InProgress("load-2")));

        var result = await step.RunAsync(_context, Guid.NewGuid(), scanId, scanManifestId);

        Assert.Equal("in-progress", result.Outcome);
        Assert.True(result.IsInProgress);

        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("started", job.Status);
        Assert.Null(job.CompletedAt);

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("started", workflow.Status);
    }

    [Fact]
    public async Task RunAsync_Failed_MarksJobFailedAndThrows()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await SeedPendingJobAsync(scanId, scanManifestId, "load-3");
        var step = BuildStep(new FakeBulkLoaderClient(Failed("load-3", "s3 access denied")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(_context, Guid.NewGuid(), scanId, scanManifestId));

        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("failed", job.Status);
        Assert.Contains("s3 access denied", job.ErrorSummary);

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("graph_load_failed", workflow.Status);
        Assert.Contains("s3 access denied", workflow.Error);
    }

    [Fact]
    public async Task RunAsync_CompletedWithRowErrors_MarksJobFailedAndThrows()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await SeedPendingJobAsync(scanId, scanManifestId, "load-4");
        var step = BuildStep(new FakeBulkLoaderClient(CompletedWithRowErrors("load-4")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.RunAsync(_context, Guid.NewGuid(), scanId, scanManifestId));

        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == scanId);
        Assert.Equal("failed", job.Status);
    }

    [Fact]
    public async Task RunAsync_ThrowsWhenCancelled()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await SeedPendingJobAsync(scanId, scanManifestId, "load-6");
        var step = BuildStep(new FakeBulkLoaderClient(InProgress("load-6")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => step.RunAsync(_context, Guid.NewGuid(), scanId, scanManifestId, runId: null, cts.Token));
    }
}
