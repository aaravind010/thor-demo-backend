using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Xunit;

namespace Thor.Workflows.Abstractions.Tests;

/// <summary>
/// Exercises <see cref="WorkflowLifecycle"/> — the `workflow` table create/update logic shared by
/// every workflow module — against a real Postgres (Testcontainers). `scan_id`/`scan_manifest_id`
/// are real foreign keys, so every test seeds a `Scan`/`ScanManifest` chain rather than using
/// arbitrary Guids (mirrors <c>ScanLifecycleTests</c>'s seed helpers).
/// </summary>
public sealed class WorkflowLifecycleTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;
    private WorkflowLifecycle _lifecycle = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _context = new TenantDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        _lifecycle = new WorkflowLifecycle(new WorkflowRepository(_context));
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

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

    [Fact]
    public async Task EnsureStartedAsync_NoExistingRow_CreatesRowWithStartedStatus()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);

        var workflowId = await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions);

        var workflow = await _context.Workflows.AsNoTracking().SingleAsync(w => w.Id == workflowId);
        Assert.Equal(scanId, workflow.ScanId);
        Assert.Equal(scanManifestId, workflow.ScanManifestId);
        Assert.Equal(WorkflowTypes.Ingestion, workflow.WorkflowType);
        Assert.Equal(WorkflowTriggers.StepFunctions, workflow.Trigger);
        Assert.Equal("started", workflow.Status);
        Assert.NotNull(workflow.StartedAt);
    }

    [Fact]
    public async Task EnsureStartedAsync_CalledTwice_IsIdempotent()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);

        var firstId = await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions);
        var secondId = await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions);

        Assert.Equal(firstId, secondId);
        var rows = await _context.Workflows.Where(w => w.ScanManifestId == scanManifestId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("started", rows[0].Status);
    }

    [Fact]
    public async Task MarkCompletedAsync_ExistingRow_SetsCompletedStatusAndTimestamp()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions);

        await _lifecycle.MarkCompletedAsync(scanManifestId, WorkflowTypes.Ingestion);

        var workflow = await _context.Workflows.AsNoTracking().SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("completed", workflow.Status);
        Assert.NotNull(workflow.CompletedAt);
    }

    [Fact]
    public async Task MarkCompletedAsync_UnknownManifest_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _lifecycle.MarkCompletedAsync(Guid.NewGuid(), WorkflowTypes.Ingestion));
    }

    [Fact]
    public async Task MarkFailedAsync_ExistingRow_SetsFailedStatusAndError()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions);

        await _lifecycle.MarkFailedAsync(scanManifestId, WorkflowTypes.Ingestion, "boom");

        var workflow = await _context.Workflows.AsNoTracking().SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("failed", workflow.Status);
        Assert.Equal("boom", workflow.Error);
        Assert.NotNull(workflow.CompletedAt);
    }

    [Fact]
    public async Task MarkFailedAsync_WithCustomStatus_UsesGivenStatusInsteadOfGenericFailed()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions);

        await _lifecycle.MarkFailedAsync(scanManifestId, WorkflowTypes.Ingestion, "boom", status: "promote_failed");

        var workflow = await _context.Workflows.AsNoTracking().SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("promote_failed", workflow.Status);
        Assert.Equal("boom", workflow.Error);
        Assert.NotNull(workflow.CompletedAt);
    }

    [Fact]
    public async Task MarkFailedAsync_UnknownManifest_IsNoOp()
    {
        await _lifecycle.MarkFailedAsync(Guid.NewGuid(), WorkflowTypes.Ingestion, "boom");
    }

    [Fact]
    public async Task MarkStatusAsync_ExistingRow_SetsGivenStatus()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions);

        await _lifecycle.MarkStatusAsync(scanManifestId, WorkflowTypes.Ingestion, "extract_and_stage_complete");

        var workflow = await _context.Workflows.AsNoTracking().SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("extract_and_stage_complete", workflow.Status);
    }

    [Fact]
    public async Task MarkStatusAsync_UnknownManifest_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _lifecycle.MarkStatusAsync(Guid.NewGuid(), WorkflowTypes.Ingestion, "extract_and_stage_complete"));
    }

    [Fact]
    public async Task MarkStatusAsync_DoesNotTouchCompletedAtOrError()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions);

        await _lifecycle.MarkStatusAsync(scanManifestId, WorkflowTypes.Ingestion, "promote_complete");

        var workflow = await _context.Workflows.AsNoTracking().SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Null(workflow.CompletedAt);
        Assert.Null(workflow.Error);
    }

    [Fact]
    public async Task EnsureStartedAsync_SameRunIdTwice_IsIdempotent()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        var runId = Guid.NewGuid();

        var firstId = await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions, runId);
        var secondId = await _lifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions, runId);

        Assert.Equal(firstId, secondId);
        var rows = await _context.Workflows.Where(w => w.ScanManifestId == scanManifestId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(runId, rows[0].RunId);
    }

    /// <summary>
    /// The core retry-vs-retrigger proof: a step-level retry within one execution reuses the same
    /// row (same RunId); a fresh re-trigger after DLQ exhaustion — a new execution, hence a new
    /// RunId — creates a *separate* row instead of overwriting the failed one, preserving history.
    /// </summary>
    [Fact]
    public async Task EnsureStartedAsync_DifferentRunIdsForSameScanManifest_CreatesSeparateRowsPreservingHistory()
    {
        var scanId = await CreateScanAsync();
        var scanManifestId = await CreateManifestAsync(scanId);
        var firstRunId = Guid.NewGuid();
        var secondRunId = Guid.NewGuid();

        var firstAttemptId = await _lifecycle.EnsureStartedAsync(
            scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions, runId: firstRunId);
        await _lifecycle.MarkFailedAsync(scanManifestId, WorkflowTypes.Ingestion, "first attempt failed", runId: firstRunId);

        var secondAttemptId = await _lifecycle.EnsureStartedAsync(
            scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions, runId: secondRunId);
        await _lifecycle.MarkCompletedAsync(scanManifestId, WorkflowTypes.Ingestion, runId: secondRunId);

        Assert.NotEqual(firstAttemptId, secondAttemptId);

        var firstAttempt = await _context.Workflows.AsNoTracking().SingleAsync(w => w.Id == firstAttemptId);
        Assert.Equal("failed", firstAttempt.Status);
        Assert.Equal("first attempt failed", firstAttempt.Error);

        var secondAttempt = await _context.Workflows.AsNoTracking().SingleAsync(w => w.Id == secondAttemptId);
        Assert.Equal("completed", secondAttempt.Status);
        Assert.Null(secondAttempt.Error);

        var rows = await _context.Workflows.Where(w => w.ScanManifestId == scanManifestId).ToListAsync();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task EnsureStartedAsync_StandaloneApiTriggered_TracksByRunIdAlone()
    {
        var runId = Guid.NewGuid();

        var workflowId = await _lifecycle.EnsureStartedAsync(
            scanId: null, scanManifestId: null, WorkflowTypes.Ingestion, WorkflowTriggers.Api, runId);
        var sameId = await _lifecycle.EnsureStartedAsync(
            scanId: null, scanManifestId: null, WorkflowTypes.Ingestion, WorkflowTriggers.Api, runId);

        Assert.Equal(workflowId, sameId);

        var workflow = await _context.Workflows.AsNoTracking().SingleAsync(w => w.Id == workflowId);
        Assert.Null(workflow.ScanId);
        Assert.Null(workflow.ScanManifestId);
        Assert.Equal(runId, workflow.RunId);
        Assert.Equal(WorkflowTriggers.Api, workflow.Trigger);

        await _lifecycle.MarkCompletedAsync(scanManifestId: null, WorkflowTypes.Ingestion, runId);
        workflow = await _context.Workflows.AsNoTracking().SingleAsync(w => w.Id == workflowId);
        Assert.Equal("completed", workflow.Status);
    }

    [Fact]
    public async Task EnsureStartedAsync_NoScanManifestIdAndNoRunId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _lifecycle.EnsureStartedAsync(scanId: null, scanManifestId: null, WorkflowTypes.Ingestion, WorkflowTriggers.Api));
    }
}
