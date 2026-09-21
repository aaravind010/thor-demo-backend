using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Graph;
using Thor.Graph.BulkLoad;
using Thor.S3;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.Steps;
using Xunit;

namespace Thor.Workflows.Ingestion.Tests.Steps;

/// <summary>
/// Exercises <see cref="GraphLoadStartStep"/> against a real Postgres (Testcontainers), with
/// fakes standing in for Neptune/S3/the bulk loader — those are exercised on their own in
/// <see cref="Graph.GraphVertexStoreTests"/>/<see cref="Graph.NeptuneBulkLoaderClientTests"/>.
/// Accounts/groups here carry no edge_refs, so edge resolution always produces zero edges —
/// keeps these tests focused on the vertex delete-vs-bulk-load partitioning.
/// </summary>
public sealed class GraphLoadStartStepTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private TenantDbContext _context = null!;
    private Guid _sourceId;
    private Guid _scanId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _context = NewContext();
        await _context.Database.EnsureCreatedAsync();
        await SeedUnclassifiedAccountTypeAsync();

        (_sourceId, _scanId) = await SeedScanChainAsync();
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

    private async Task SeedUnclassifiedAccountTypeAsync()
    {
        _context.AccountTypes.Add(new AccountType
        {
            Id = WellKnownAccountTypes.Unclassified,
            Name = "Unclassified",
            Description = "Default account type for newly-promoted accounts pending classification.",
            IsHuman = false,
        });
        await _context.SaveChangesAsync();
    }

    private async Task<(Guid SourceId, Guid ScanId)> SeedScanChainAsync()
    {
        var authMethod = new AuthenticationMethod { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Name = "test-auth" };
        _context.AuthenticationMethods.Add(authMethod);

        var scanConfig = new ScanConfig
        {
            Id = Guid.NewGuid(), Name = "test-scan-config", AuthMethodId = authMethod.Id,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = "test", UpdatedBy = "test",
        };
        _context.ScanConfigs.Add(scanConfig);

        var scan = new Scan { Id = Guid.NewGuid(), ScanConfigId = scanConfig.Id, ScanType = "initial", Status = "running" };
        _context.Scans.Add(scan);

        var source = new Source
        {
            Id = Guid.NewGuid(), ConnectorType = ConnectorTypes.ActiveDirectory, Name = "test-source",
            Config = "{}", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _context.Sources.Add(source);

        await _context.SaveChangesAsync();
        return (source.Id, scan.Id);
    }

    private async Task<Guid> SeedScanManifestAsync()
    {
        var manifest = new ScanManifest
        {
            Id = Guid.NewGuid(), ScanId = _scanId, FileLocations = [], Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _context.ScanManifests.Add(manifest);
        await _context.SaveChangesAsync();
        return manifest.Id;
    }

    private async Task<Guid> InsertAccountAsync(
        Guid scanManifestId, string nativeId, bool isDeleted,
        string? aliasKey = null, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context.Accounts.Add(new Account
        {
            Id = id, SourceId = _sourceId, ConnectorType = ConnectorTypes.ActiveDirectory, NativeId = nativeId,
            AccountKind = "user", IsHuman = true, DisplayName = nativeId, SamAccountName = nativeId,
            Upn = $"{nativeId}@test.com", Email = $"{nativeId}@test.com", DomainName = "test.com", FilerName = "",
            NativeAccountId = "S-1-5-1", IsDeleted = isDeleted, IsDisabled = false,
            AccountTypeId = WellKnownAccountTypes.Unclassified,
            RawAttributes = aliasKey is null ? """{"AliasKeys":[],"EdgeRefs":[]}""" : RawAttributesJson(aliasKey, edgeRefs),
            ContentHash = "hash-" + nativeId, HashVersion = 0,
            CreatedAt = now, UpdatedAt = now,
        });
        if (aliasKey is not null)
        {
            _context.EntityAliases.Add(new EntityAlias
            {
                Id = Guid.NewGuid(), EntityType = "account", EntityId = id, SourceId = _sourceId,
                ConnectorType = ConnectorTypes.ActiveDirectory, AliasKey = aliasKey, CreatedAt = now,
            });
        }
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = scanManifestId, ScanId = _scanId, EntityType = "account",
            EntityId = id, ChangeType = isDeleted ? "deleted" : "inserted", OccurredAt = now,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> InsertGroupAsync(
        Guid scanManifestId, string nativeId, bool isDeleted,
        string? aliasKey = null, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context.Grps.Add(new Grp
        {
            Id = id, SourceId = _sourceId, ConnectorType = ConnectorTypes.ActiveDirectory, NativeId = nativeId,
            GroupClass = "security_global", DisplayName = nativeId, Email = null, DomainName = "test.com",
            IsLargeGroup = false, IsDeleted = isDeleted,
            RawAttributes = aliasKey is null ? """{"AliasKeys":[],"EdgeRefs":[]}""" : RawAttributesJson(aliasKey, edgeRefs),
            ContentHash = "hash-" + nativeId, HashVersion = 0,
            CreatedAt = now, UpdatedAt = now,
        });
        if (aliasKey is not null)
        {
            _context.EntityAliases.Add(new EntityAlias
            {
                Id = Guid.NewGuid(), EntityType = "grp", EntityId = id, SourceId = _sourceId,
                ConnectorType = ConnectorTypes.ActiveDirectory, AliasKey = aliasKey, CreatedAt = now,
            });
        }
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = scanManifestId, ScanId = _scanId, EntityType = "grp",
            EntityId = id, ChangeType = isDeleted ? "deleted" : "inserted", OccurredAt = now,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> InsertAssetAsync(Guid scanManifestId, string nativeId)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context.Assets.Add(new Asset
        {
            Id = id, SourceId = _sourceId, ConnectorType = ConnectorTypes.CyberArk, NativeId = nativeId,
            AssetType = "safe", DisplayName = nativeId, FullPath = $"/{nativeId}", FilerName = "",
            FileSize = 0, FileCount = 0, BrokenAcl = false, IsProtected = true, ParentAssetId = null,
            RawAttributes = """{"AliasKeys":[],"EdgeRefs":[]}""", ContentHash = "hash-" + nativeId,
            HashVersion = 0, CreatedAt = now, UpdatedAt = now,
        });
        _context.IngestChangeEvents.Add(new IngestChangeEvent
        {
            Id = Guid.NewGuid(), ScanManifestId = scanManifestId, ScanId = _scanId, EntityType = "asset",
            EntityId = id, ChangeType = "inserted", OccurredAt = now,
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private static string RawAttributesJson(string aliasKey, params (string Rel, string Dir, string Key, string? TargetType)[] edgeRefs)
    {
        var refsJson = string.Join(",", edgeRefs.Select(r =>
            $$"""{"Rel":"{{r.Rel}}","Dir":"{{r.Dir}}","Key":"{{r.Key}}","TargetType":{{(r.TargetType is null ? "null" : $"\"{r.TargetType}\"")}}}"""));
        return $$"""{"AliasKeys":["{{aliasKey}}"],"EdgeRefs":[{{refsJson}}]}""";
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

    private sealed class FakeS3ObjectStore : IS3ObjectStore
    {
        private readonly object _lock = new();
        public List<(string Bucket, string Key, string Content)> PutObjects { get; } = [];
        public Task<IReadOnlyList<string>> ListKeysAsync(string bucket, string prefix, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> GetObjectAsync(string bucket, string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> GetObjectSizeAsync(string bucket, string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PutObjectAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken = default)
        {
            lock (_lock) { PutObjects.Add((bucket, key, System.Text.Encoding.UTF8.GetString(content))); }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBulkLoaderClient : INeptuneBulkLoaderClient
    {
        public List<Uri> StartedSources { get; } = [];
        public string LoadIdToReturn { get; set; } = "fake-load-id";

        public Task<BulkLoadStartResult> StartLoadAsync(Uri s3SourceUri, string iamRoleArn, string region, CancellationToken cancellationToken = default)
        {
            StartedSources.Add(s3SourceUri);
            return Task.FromResult(new BulkLoadStartResult(LoadIdToReturn));
        }

        public Task<BulkLoadStatusResult> GetLoadStatusAsync(string loadId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("GraphLoadStartStep never polls.");
    }

    private sealed class ThrowingBulkLoaderClient : INeptuneBulkLoaderClient
    {
        public Task<BulkLoadStartResult> StartLoadAsync(Uri s3SourceUri, string iamRoleArn, string region, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated Neptune bulk-load-start failure");

        public Task<BulkLoadStatusResult> GetLoadStatusAsync(string loadId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("GraphLoadStartStep never polls.");
    }

    /// <summary>Stands in for GraphLoadPollStep's Neptune dependency in tests that only exercise the deferred-delete path, not the poll loop itself.</summary>
    private sealed class CompletedBulkLoaderClient : INeptuneBulkLoaderClient
    {
        public Task<BulkLoadStartResult> StartLoadAsync(Uri s3SourceUri, string iamRoleArn, string region, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("GraphLoadPollStep never starts a load.");

        public Task<BulkLoadStatusResult> GetLoadStatusAsync(string loadId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BulkLoadStatusResult(loadId, BulkLoadStatus.Completed, "LOAD_COMPLETED", 1, 0, 0, 0, []));
    }

    private sealed class FakeTenantConnectionManager(Func<Guid, TenantDbContext> factory) : ITenantConnectionManager
    {
        public Task<Npgsql.NpgsqlConnection> GetValidatedConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<TenantDbContext> GetTenantDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(factory(tenantId));
    }

    private GraphLoadStartStep BuildStep(
        FakeGraphVertexStore vertexStore, FakeS3ObjectStore s3, INeptuneBulkLoaderClient bulkLoader, TimeSpan? startStaleThreshold = null) =>
        new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => _context), vertexStore, new FakeGraphEdgeStore(),
            s3, bulkLoader, "test-bucket", "arn:aws:iam::123:role/neptune-load", "us-east-1", startStaleThreshold ?? TimeSpan.FromMinutes(10));

    [Fact]
    public async Task RunAsync_ChangedAccountNotDeleted_UploadsCsvAndStartsBulkLoad()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAccountAsync(scanManifestId, "alice", isDeleted: false);

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal(0, result.VerticesDeleted);
        Assert.Equal(1, result.VerticesQueuedForLoad);
        Assert.Equal("fake-load-id", result.LoadId);
        Assert.Empty(vertexStore.Deleted);
        Assert.Single(s3.PutObjects);
        Assert.Contains("alice", s3.PutObjects[0].Content);
        Assert.Single(bulkLoader.StartedSources);

        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == _scanId);
        Assert.Equal("started", job.Status);
        Assert.Equal("fake-load-id", job.LoadId);

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("graph_load_started", workflow.Status);
    }

    [Fact]
    public async Task RunAsync_ThrowsWhenCancelled()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAccountAsync(scanManifestId, "alice", isDeleted: false);

        var step = BuildStep(new FakeGraphVertexStore(), new FakeS3ObjectStore(), new FakeBulkLoaderClient());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => step.RunAsync(_context, tenantId, _scanId, scanManifestId, runId: null, cts.Token));
    }

    [Fact]
    public async Task RunAsync_DeletedAccount_DeletesVertexDirectly_DoesNotStartBulkLoad()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        var accountId = await InsertAccountAsync(scanManifestId, "bob", isDeleted: true);

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal(1, result.VerticesDeleted);
        Assert.Equal(0, result.VerticesQueuedForLoad);
        Assert.Null(result.LoadId);
        Assert.Single(vertexStore.Deleted);
        Assert.Equal((tenantId, "account", accountId), vertexStore.Deleted[0]);
        Assert.Empty(s3.PutObjects);
        Assert.Empty(bulkLoader.StartedSources);
        Assert.False(await _context.GraphBulkLoadJobs.AnyAsync(j => j.ScanId == _scanId));
    }

    [Fact]
    public async Task RunAsync_MixedChangedAndDeletedAccounts_DefersDeleteUntilPollConfirmsSuccess()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAccountAsync(scanManifestId, "alice", isDeleted: false);
        var deletedAccountId = await InsertAccountAsync(scanManifestId, "bob", isDeleted: true);

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var startResult = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        // The delete is queued alongside a load, so it must NOT be applied yet.
        Assert.Equal(0, startResult.VerticesDeleted);
        Assert.Equal(1, startResult.VerticesQueuedForLoad);
        Assert.Empty(vertexStore.Deleted);
        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == _scanId);
        Assert.Equal("started", job.Status);
        Assert.NotNull(job.PendingVertexDeletes);

        var pollStep = new GraphLoadPollStep(
            NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => _context),
            vertexStore, new FakeGraphEdgeStore(), new CompletedBulkLoaderClient());

        var pollResult = await pollStep.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal(1, pollResult.VerticesDeleted);
        Assert.Single(vertexStore.Deleted);
        Assert.Equal((tenantId, "account", deletedAccountId), vertexStore.Deleted[0]);
        var completedJob = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == _scanId);
        Assert.Equal("completed", completedJob.Status);
        Assert.Null(completedJob.PendingVertexDeletes);
    }

    [Fact]
    public async Task RunAsync_NothingChanged_ReturnsZeroCounts_DoesNotTouchS3OrBulkLoader()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, Guid.NewGuid(), _scanId, scanManifestId);

        Assert.Equal(0, result.VerticesDeleted);
        Assert.Equal(0, result.VerticesQueuedForLoad);
        Assert.Null(result.LoadId);
        Assert.Empty(s3.PutObjects);
        Assert.Empty(bulkLoader.StartedSources);

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("graph_load_started", workflow.Status);
    }

    [Fact]
    public async Task RunAsync_PendingJobAlreadyExistsForScan_DoesNotStartAnother()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAccountAsync(scanManifestId, "carol", isDeleted: false);
        _context.GraphBulkLoadJobs.Add(new GraphBulkLoadJob
        {
            Id = Guid.NewGuid(), ScanId = _scanId, ScanManifestId = scanManifestId, LoadId = "already-running", S3Uri = "s3://bucket/prefix/",
            Status = "started", StartedAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal("already-running", result.LoadId);
        Assert.Empty(s3.PutObjects);
        Assert.Empty(bulkLoader.StartedSources);

        var workflow = await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId);
        Assert.Equal("graph_load_started", workflow.Status);
    }

    [Fact]
    public async Task RunAsync_ReservedJobAlreadyExistsForScan_DoesNotStartAnother()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAccountAsync(scanManifestId, "carol", isDeleted: false);
        _context.GraphBulkLoadJobs.Add(new GraphBulkLoadJob
        {
            Id = Guid.NewGuid(), ScanId = _scanId, ScanManifestId = scanManifestId, LoadId = null, S3Uri = "s3://bucket/prefix/",
            Status = "starting", StartedAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Null(result.LoadId);
        Assert.Empty(s3.PutObjects);
        Assert.Empty(bulkLoader.StartedSources);
    }

    [Fact]
    public async Task RunAsync_PendingJobForDifferentManifest_StartsAnotherAnyway()
    {
        var otherManifestId = await SeedScanManifestAsync();
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAccountAsync(scanManifestId, "carol", isDeleted: false);
        _context.GraphBulkLoadJobs.Add(new GraphBulkLoadJob
        {
            Id = Guid.NewGuid(), ScanId = _scanId, ScanManifestId = otherManifestId, LoadId = "other-manifest-running", S3Uri = "s3://bucket/other/",
            Status = "started", StartedAt = DateTimeOffset.UtcNow,
        });
        await _context.SaveChangesAsync();

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal("fake-load-id", result.LoadId);
        Assert.Single(s3.PutObjects);
        Assert.Single(bulkLoader.StartedSources);
        var job = await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanManifestId == scanManifestId);
        Assert.Equal("started", job.Status);
        Assert.Equal("fake-load-id", job.LoadId);
    }

    [Fact]
    public async Task RunAsync_StaleStartingJobForSameManifest_ReclaimsAndStartsFresh()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAccountAsync(scanManifestId, "carol", isDeleted: false);
        var staleJobId = Guid.NewGuid();
        _context.GraphBulkLoadJobs.Add(new GraphBulkLoadJob
        {
            Id = staleJobId, ScanId = _scanId, ScanManifestId = scanManifestId, LoadId = null, S3Uri = "s3://bucket/stale/",
            Status = "starting", StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
        });
        await _context.SaveChangesAsync();

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader, startStaleThreshold: TimeSpan.FromSeconds(1));

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal("fake-load-id", result.LoadId);
        Assert.Single(bulkLoader.StartedSources);

        var staleJob = await _context.GraphBulkLoadJobs.SingleAsync(j => j.Id == staleJobId);
        Assert.Equal("failed", staleJob.Status);
        Assert.NotNull(staleJob.ErrorSummary);

        var freshJob = await _context.GraphBulkLoadJobs.SingleAsync(j => j.Id != staleJobId && j.ScanManifestId == scanManifestId);
        Assert.Equal("started", freshJob.Status);
        Assert.Equal("fake-load-id", freshJob.LoadId);
    }

    [Fact]
    public async Task RunAsync_DeletedGroup_DeletesVertexDirectly_DoesNotStartBulkLoad()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        var groupId = await InsertGroupAsync(scanManifestId, "eng-team", isDeleted: true);

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal(1, result.VerticesDeleted);
        Assert.Equal(0, result.VerticesQueuedForLoad);
        Assert.Null(result.LoadId);
        Assert.Single(vertexStore.Deleted);
        Assert.Equal((tenantId, "grp", groupId), vertexStore.Deleted[0]);
        Assert.Empty(s3.PutObjects);
        Assert.Empty(bulkLoader.StartedSources);
    }

    [Fact]
    public async Task RunAsync_ChangedAccountGroupAndResolvableEdge_UploadsAllThreeCsvsConcurrently()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertGroupAsync(scanManifestId, "eng-team", isDeleted: false, aliasKey: "cn=eng-team,dc=test");
        await InsertAccountAsync(scanManifestId, "dave", isDeleted: false, aliasKey: "cn=dave,dc=test",
            edgeRefs: [("MEMBER_OF", "out", "cn=eng-team,dc=test", "grp")]);

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal(2, result.VerticesQueuedForLoad);
        Assert.Equal(1, result.EdgesQueuedForLoad);
        Assert.Equal(3, s3.PutObjects.Count);
        var keys = s3.PutObjects.Select(p => p.Key).ToHashSet();
        Assert.Contains(keys, k => k.EndsWith("/vertices/accounts.csv"));
        Assert.Contains(keys, k => k.EndsWith("/vertices/groups.csv"));
        Assert.Contains(keys, k => k.EndsWith("/edges/edges.csv"));
    }

    [Fact]
    public async Task RunAsync_ChangedAssetNotDeleted_UploadsCsvAndStartsBulkLoad()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAssetAsync(scanManifestId, "safe-1");

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal(0, result.VerticesDeleted);
        Assert.Equal(1, result.VerticesQueuedForLoad);
        Assert.Equal("fake-load-id", result.LoadId);
        Assert.Empty(vertexStore.Deleted);
        Assert.Single(s3.PutObjects);
        Assert.Contains(s3.PutObjects, p => p.Key.EndsWith("/vertices/assets.csv"));
        Assert.Contains("safe-1", s3.PutObjects[0].Content);
        Assert.Single(bulkLoader.StartedSources);
    }

    [Fact]
    public async Task RunAsync_NoAssetChanges_DoesNotUploadAssetCsv()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertAccountAsync(scanManifestId, "erin", isDeleted: false);

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        var keys = s3.PutObjects.Select(p => p.Key).ToHashSet();
        Assert.Contains(keys, k => k.EndsWith("/vertices/accounts.csv"));
        Assert.DoesNotContain(keys, k => k.EndsWith("/vertices/assets.csv"));
    }

    [Fact]
    public async Task RunAsync_ChangedAccountGroupAssetAndResolvableEdge_UploadsAllFourCsvsConcurrently()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertGroupAsync(scanManifestId, "eng-team-2", isDeleted: false, aliasKey: "cn=eng-team-2,dc=test");
        await InsertAccountAsync(scanManifestId, "frank", isDeleted: false, aliasKey: "cn=frank,dc=test",
            edgeRefs: [("MEMBER_OF", "out", "cn=eng-team-2,dc=test", "grp")]);
        await InsertAssetAsync(scanManifestId, "safe-2");

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var bulkLoader = new FakeBulkLoaderClient();
        var step = BuildStep(vertexStore, s3, bulkLoader);

        var result = await step.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal(3, result.VerticesQueuedForLoad);
        Assert.Equal(1, result.EdgesQueuedForLoad);
        Assert.Equal(4, s3.PutObjects.Count);
        var keys = s3.PutObjects.Select(p => p.Key).ToHashSet();
        Assert.Contains(keys, k => k.EndsWith("/vertices/accounts.csv"));
        Assert.Contains(keys, k => k.EndsWith("/vertices/groups.csv"));
        Assert.Contains(keys, k => k.EndsWith("/vertices/assets.csv"));
        Assert.Contains(keys, k => k.EndsWith("/edges/edges.csv"));
    }

    [Fact]
    public async Task RunAsync_RetryAfterBulkLoadStartThrows_StillLoadsEdgesCommittedByFirstAttempt()
    {
        var scanManifestId = await SeedScanManifestAsync();
        var tenantId = Guid.NewGuid();
        await InsertGroupAsync(scanManifestId, "eng-team-3", isDeleted: false, aliasKey: "cn=eng-team-3,dc=test");
        await InsertAccountAsync(scanManifestId, "grace", isDeleted: false, aliasKey: "cn=grace,dc=test",
            edgeRefs: [("MEMBER_OF", "out", "cn=eng-team-3,dc=test", "grp")]);

        var vertexStore = new FakeGraphVertexStore();
        var s3 = new FakeS3ObjectStore();
        var firstAttempt = BuildStep(vertexStore, s3, new ThrowingBulkLoaderClient());

        // Edge resolution commits; a job row is reserved ("starting") before the Neptune call,
        // then marked "failed" when that call throws — a retry must see this row as terminal,
        // not still-pending, or it would be permanently blocked from ever starting a new load.
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstAttempt.RunAsync(_context, tenantId, _scanId, scanManifestId));

        var edge = await _context.Edges.SingleAsync(e => e.RelType == "MEMBER_OF");
        Assert.False(edge.IsDeleted);
        Assert.Equal("failed", (await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == _scanId)).Status);
        Assert.Equal("graph_load_failed", (await _context.Workflows.SingleAsync(w => w.ScanManifestId == scanManifestId)).Status);
        // CSVs upload before the Neptune StartLoadAsync call that throws, so they're already
        // present at this point — Neptune needs them in S3 before it can be told to bulk-load them.
        Assert.NotEmpty(s3.PutObjects);

        // Retry with the same scanId/scanManifestId; this time the bulk loader succeeds.
        var workingLoader = new FakeBulkLoaderClient();
        var retryAttempt = BuildStep(vertexStore, s3, workingLoader);
        var result = await retryAttempt.RunAsync(_context, tenantId, _scanId, scanManifestId);

        Assert.Equal(1, result.EdgesQueuedForLoad);
        Assert.Single(workingLoader.StartedSources);
        Assert.Contains(s3.PutObjects, p => p.Key.EndsWith("/edges/edges.csv"));
        // The failed first-attempt row and the retry's new row now both exist for this scan.
        Assert.Equal("started", (await _context.GraphBulkLoadJobs.SingleAsync(j => j.ScanId == _scanId && j.Status != "failed")).Status);
    }
}
