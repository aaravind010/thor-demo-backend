using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Ingestion.Orchestration;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Orchestration;

/// <summary>
/// Integration test against a real Postgres (via Testcontainers) — <see cref="ScanRepository"/>/
/// <see cref="ScanManifestRepository"/> are thin EF wrappers, not worth faking. Requires Docker
/// running locally.
///
/// Note: <see cref="ScanLifecycle.MarkManifestStatusAsync"/> has no production call site anywhere
/// in the pipeline today (confirmed by repo-wide search) — still tested here since it's public API
/// on a class the pipeline depends on, not because anything currently invokes it.
/// </summary>
public sealed class ScanLifecycleTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;
    private ScanLifecycle _lifecycle = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        _context = new TenantDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        _lifecycle = new ScanLifecycle(new ScanRepository(_context), new ScanManifestRepository(_context));
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task<Guid> CreateScanAsync(string scanType)
    {
        var authMethod = new AuthenticationMethod { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Name = "test-auth" };
        var scanConfig = new ScanConfig
        {
            Id = Guid.NewGuid(), Name = "test-scan-config-" + scanType, AuthMethodId = authMethod.Id,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = "test", UpdatedBy = "test",
        };
        var scan = new Scan { Id = Guid.NewGuid(), ScanConfigId = scanConfig.Id, ScanType = scanType, Status = "running" };
        _context.AuthenticationMethods.Add(authMethod);
        _context.ScanConfigs.Add(scanConfig);
        _context.Scans.Add(scan);
        await _context.SaveChangesAsync();
        return scan.Id;
    }

    private async Task<(Guid ManifestId, DateTimeOffset SeededUpdatedAt)> CreateManifestAsync(Guid scanId, string status)
    {
        var seededUpdatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(), ScanId = scanId, FileLocations = [], Status = status,
            CreatedAt = seededUpdatedAt, UpdatedAt = seededUpdatedAt,
        };
        _context.ScanManifests.Add(manifest);
        await _context.SaveChangesAsync();
        return (manifest.Id, seededUpdatedAt);
    }

    [Fact]
    public async Task IsInitialScanAsync_InitialScan_ReturnsTrue()
    {
        var scanId = await CreateScanAsync("initial");

        Assert.True(await _lifecycle.IsInitialScanAsync(scanId));
    }

    [Fact]
    public async Task IsInitialScanAsync_IncrementalScan_ReturnsFalse()
    {
        var scanId = await CreateScanAsync("incremental");

        Assert.False(await _lifecycle.IsInitialScanAsync(scanId));
    }

    [Fact]
    public async Task IsInitialScanAsync_UnknownScanId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _lifecycle.IsInitialScanAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkManifestStatusAsync_UpdatesStatusAndUpdatedAt()
    {
        var scanId = await CreateScanAsync("initial");
        var (manifestId, seededUpdatedAt) = await CreateManifestAsync(scanId, "pending");

        await _lifecycle.MarkManifestStatusAsync(manifestId, "ingested");

        var manifest = await _context.ScanManifests.AsNoTracking().SingleAsync(m => m.Id == manifestId);
        Assert.Equal("ingested", manifest.Status);
        Assert.True(manifest.UpdatedAt > seededUpdatedAt);
    }

    [Fact]
    public async Task MarkManifestStatusAsync_UnknownManifestId_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _lifecycle.MarkManifestStatusAsync(Guid.NewGuid(), "ingested"));
    }
}
