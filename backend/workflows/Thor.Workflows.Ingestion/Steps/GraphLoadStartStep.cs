using System.Text;
using System.Text.Json;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Graph;
using Thor.Graph.BulkLoad;
using Thor.S3;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Ingestion.Composition;
using Thor.Workflows.Ingestion.Constants;
using Thor.Workflows.Ingestion.EdgeGate;
using Thor.Workflows.Ingestion.Orchestration;

namespace Thor.Workflows.Ingestion.Steps;

public sealed record GraphLoadStartResult(
    int VerticesDeleted, int EdgesDeleted, int VerticesQueuedForLoad, int EdgesQueuedForLoad, string? LoadId);

/// <summary>
/// First half of "edge resolution" (renamed graph load, since it covers vertices too): resolves
/// edges (Postgres-only, unchanged), then for every changed vertex/edge either deletes it
/// directly via Gremlin (bulk load can't do deletes) or writes it to a Gremlin bulk-load CSV in
/// S3 and starts a Neptune bulk load job. <see cref="GraphLoadPollStep"/> is the second half.
/// </summary>
public sealed class GraphLoadStartStep : IWorkflowStep
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;
    private readonly IGraphVertexStore _vertexStore;
    private readonly IGraphEdgeStore _edgeStore;
    private readonly IS3ObjectStore _s3;
    private readonly INeptuneBulkLoaderClient _bulkLoader;
    private readonly string _bucket;
    private readonly string _iamRoleArn;
    private readonly string _region;
    private readonly TimeSpan _startStaleThreshold;

    /// <summary>Test seam — lets tests substitute fakes for every external dependency the parameterless constructor builds for real.</summary>
    public GraphLoadStartStep(
        ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager,
        IGraphVertexStore vertexStore, IGraphEdgeStore edgeStore, IS3ObjectStore s3,
        INeptuneBulkLoaderClient bulkLoader, string bucket, string iamRoleArn, string region,
        TimeSpan startStaleThreshold)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
        _vertexStore = vertexStore;
        _edgeStore = edgeStore;
        _s3 = s3;
        _bulkLoader = bulkLoader;
        _bucket = bucket;
        _iamRoleArn = iamRoleArn;
        _region = region;
        _startStaleThreshold = startStaleThreshold;
    }

    public GraphLoadStartStep()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _tenantConnectionManager = TenantConnectionManagerFactory.Build();

        _region = Environment.GetEnvironmentVariable("THOR_AWS_REGION") ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? throw new InvalidOperationException("THOR_AWS_REGION or AWS_REGION is required.");
        var neptuneOptions = new NeptuneOptions
        {
            Endpoint = Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENDPOINT") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_PORT"), out var neptunePort) ? neptunePort : 8182,
            EnableSsl = bool.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENABLESSL"), out var neptuneSsl) ? neptuneSsl : true,
            Region = _region,
        };
        _vertexStore = new GraphVertexStore(neptuneOptions);
        _edgeStore = new GraphEdgeStore(neptuneOptions);
        _s3 = new S3ObjectStore(new AmazonS3Client());
        _bulkLoader = new NeptuneBulkLoaderClient(neptuneOptions);
        _bucket = Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_BUCKET")
            ?? throw new InvalidOperationException("THOR_GRAPH_BULKLOAD_BUCKET is required.");
        _iamRoleArn = Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN")
            ?? throw new InvalidOperationException("THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN is required.");
        _startStaleThreshold = TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_START_STALE_SECONDS"), out var staleSeconds) ? staleSeconds : 300);
    }

    public async Task<(object? Result, bool IsInProgress)> ExecuteAsync(string inputJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<IngestionRequest>(inputJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{WorkflowHost.InputEnvVar} did not deserialize to a valid IngestionRequest.");

        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        var result = await RunAsync(db, request.TenantId, request.ScanId, request.ScanManifestId, request.RunId, cancellationToken);
        return (result, false);
    }

    public async Task<GraphLoadStartResult> RunAsync(
        TenantDbContext db, Guid tenantId, Guid scanId, Guid scanManifestId, Guid? runId = null, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<GraphLoadStartStep>();

        var workflowLifecycle = new WorkflowLifecycle(new WorkflowRepository(db));
        await workflowLifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions, runId, cancellationToken);

        GraphBulkLoadJob? reservedJob = null;
        try
        {
            // Idempotency: a retried invocation for the same scan manifest shouldn't start a
            // second load job while one is still being reserved or is pending (mirrors the
            // tenant+scanId idempotency-key pattern used elsewhere for CDC — see ADR §12).
            // Scoped by (ScanId, ScanManifestId) — a scan can have many manifests, and each
            // manifest's bulk load is independent of its siblings' (mirrors WorkflowEntity).
            // "starting" covers the window between reserving a job row and Neptune actually
            // accepting the load.
            var existing = await db.GraphBulkLoadJobs
                .Where(j => j.ScanId == scanId && j.ScanManifestId == scanManifestId && (j.Status == "starting" || j.Status == "started"))
                .OrderByDescending(j => j.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                var isStaleReservation = existing.Status == "starting" && DateTimeOffset.UtcNow - existing.StartedAt > _startStaleThreshold;
                if (!isStaleReservation)
                {
                    logger.LogInformation(
                        "Scan {ScanId} manifest {ScanManifestId} already has a pending bulk load ({Status}) — not starting another.",
                        scanId, scanManifestId, existing.Status);
                    await workflowLifecycle.MarkStatusAsync(scanManifestId, WorkflowTypes.Ingestion, IngestionStatuses.GraphLoadStarted, runId, cancellationToken);
                    return new GraphLoadStartResult(0, 0, 0, 0, existing.LoadId);
                }

                // The reservation never reached Neptune (no LoadId) and no container-crash
                // catch block ever ran for it — likely a killed ECS task. Reclaim it as failed
                // so this invocation can start a fresh attempt instead of blocking forever.
                logger.LogWarning(
                    "Scan {ScanId} manifest {ScanManifestId} had a stale 'starting' bulk load job {JobId} (reserved at {StartedAt}, never reached Neptune) — marking it failed and starting a fresh attempt.",
                    scanId, scanManifestId, existing.Id, existing.StartedAt);
                existing.Status = "failed";
                existing.CompletedAt = DateTimeOffset.UtcNow;
                existing.ErrorSummary = "Abandoned: exceeded starting-phase staleness threshold without reaching Neptune.";
                await db.SaveChangesAsync(cancellationToken);
            }

            var edgeResolver = new EdgeResolver(new OneSidedRefIndex(), _loggerFactory.CreateLogger<EdgeResolver>());
            var scanLifecycle = new ScanLifecycle(new ScanRepository(db), new ScanManifestRepository(db));

            var isInitial = await scanLifecycle.IsInitialScanAsync(scanId, cancellationToken);
            _ = isInitial
                ? await edgeResolver.ResolveFullAsync(db, scanId, scanManifestId, RelTypes.TwoSided, cancellationToken)
                : await edgeResolver.ResolveIncrementalAsync(db, scanId, scanManifestId, RelTypes.TwoSided, cancellationToken);

            var (accountsToDelete, accountsToLoad) = await PartitionAccountsAsync(db, scanId, scanManifestId, cancellationToken);
            var (groupsToDelete, groupsToLoad) = await PartitionGroupsAsync(db, scanId, scanManifestId, cancellationToken);
            var assetsToLoad = await PartitionAssetsAsync(db, scanId, scanManifestId, cancellationToken);
            var (edgesToDelete, edgesToLoad) = await PartitionEdgesAsync(db, scanId, scanManifestId, cancellationToken);

            var vertexCount = accountsToLoad.Count + groupsToLoad.Count + assetsToLoad.Count;
            var vertexDeletes = accountsToDelete.Concat(groupsToDelete).ToList();

            if (vertexCount == 0 && edgesToLoad.Count == 0)
            {
                // Nothing queued to bulk-load — no async operation for deletes to be gated on, so
                // applying them immediately here is safe; only the mixed delete+load case below
                // needs the deletes deferred until the load is confirmed complete.
                var verticesDeletedNow = await GraphDeleteExecutor.DeleteVerticesAsync(_vertexStore, tenantId, vertexDeletes, cancellationToken);
                var edgesDeletedNow = await GraphDeleteExecutor.DeleteEdgesAsync(_edgeStore, tenantId, edgesToDelete, cancellationToken);
                logger.LogInformation(
                    "Graph load start for scan {ScanId}: nothing to bulk-load ({VerticesDeleted} vertex delete(s), {EdgesDeleted} edge delete(s)).",
                    scanId, verticesDeletedNow, edgesDeletedNow);
                await workflowLifecycle.MarkStatusAsync(scanManifestId, WorkflowTypes.Ingestion, IngestionStatuses.GraphLoadStarted, runId, cancellationToken);
                return new GraphLoadStartResult(verticesDeletedNow, edgesDeletedNow, 0, 0, null);
            }

            var attemptId = Guid.NewGuid();
            var s3Prefix = $"graph-bulk-load/{tenantId:N}/{scanId:N}/{attemptId:N}";
            var s3Uri = new Uri($"s3://{_bucket}/{s3Prefix}/");

            // Reserve the job row BEFORE calling Neptune (not after) — a crash between
            // StartLoadAsync succeeding and this row being persisted would otherwise let a retry
            // start a second Neptune load of the same data. The unique index on (ScanId,
            // ScanManifestId) for "starting"/"started" rows (TenantDbContext.OnModelCreating)
            // makes this insert race-proof against genuinely concurrent invocations too, not
            // just crash-retries.
            // Pending deletes are snapshotted now (not recomputed later): Account/Grp.IsDeleted
            // can be flipped by a different, later scan of the same source before this load
            // finishes, so recomputing at poll time could resurrect or skip a delete.
            reservedJob = new GraphBulkLoadJob
            {
                Id = attemptId,
                ScanId = scanId,
                ScanManifestId = scanManifestId,
                LoadId = null,
                S3Uri = s3Uri.ToString(),
                Status = "starting",
                StartedAt = DateTimeOffset.UtcNow,
                PendingVertexDeletes = vertexDeletes.Count == 0 ? null : JsonSerializer.Serialize(vertexDeletes),
                PendingEdgeDeletes = edgesToDelete.Count == 0 ? null : JsonSerializer.Serialize(edgesToDelete),
            };

            var reservedId = await InsertJobIfAbsentAsync(db, reservedJob, cancellationToken);
            if (reservedId is null)
            {
                // Lost the race to a concurrent invocation for this scan manifest — the unique
                // index rejected our reservation; the winner's row is now the canonical pending job.
                reservedJob = null;
                var winner = await db.GraphBulkLoadJobs
                    .Where(j => j.ScanId == scanId && j.ScanManifestId == scanManifestId && (j.Status == "starting" || j.Status == "started"))
                    .OrderByDescending(j => j.StartedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                logger.LogInformation(
                    "Scan {ScanId} manifest {ScanManifestId} already has a pending bulk load (lost reservation race) — not starting another.",
                    scanId, scanManifestId);
                await workflowLifecycle.MarkStatusAsync(scanManifestId, WorkflowTypes.Ingestion, IngestionStatuses.GraphLoadStarted, runId, cancellationToken);
                return new GraphLoadStartResult(0, 0, 0, 0, winner?.LoadId);
            }
            db.GraphBulkLoadJobs.Attach(reservedJob);

            var uploadTasks = new List<Task>();
            if (accountsToLoad.Count > 0)
            {
                uploadTasks.Add(UploadVertexCsvAsync(tenantId, "account", accountsToLoad, $"{s3Prefix}/vertices/accounts.csv", cancellationToken));
            }
            if (groupsToLoad.Count > 0)
            {
                uploadTasks.Add(UploadVertexCsvAsync(tenantId, "grp", groupsToLoad, $"{s3Prefix}/vertices/groups.csv", cancellationToken));
            }
            if (assetsToLoad.Count > 0)
            {
                uploadTasks.Add(UploadVertexCsvAsync(tenantId, "asset", assetsToLoad, $"{s3Prefix}/vertices/assets.csv", cancellationToken));
            }
            if (edgesToLoad.Count > 0)
            {
                uploadTasks.Add(UploadEdgeCsvAsync(tenantId, edgesToLoad, $"{s3Prefix}/edges/edges.csv", cancellationToken));
            }
            await Task.WhenAll(uploadTasks);

            var startResult = await _bulkLoader.StartLoadAsync(s3Uri, _iamRoleArn, _region, cancellationToken);

            reservedJob.LoadId = startResult.LoadId;
            reservedJob.Status = "started";
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Graph load start for scan {ScanId}: started load {LoadId} ({VertexCount} vertex/{EdgeCount} edge row(s)), " +
                "{VerticesQueuedForDelete} vertex delete(s)/{EdgesQueuedForDelete} edge delete(s) deferred until poll confirms success.",
                scanId, startResult.LoadId, vertexCount, edgesToLoad.Count, vertexDeletes.Count, edgesToDelete.Count);

            await workflowLifecycle.MarkStatusAsync(scanManifestId, WorkflowTypes.Ingestion, IngestionStatuses.GraphLoadStarted, runId, cancellationToken);
            return new GraphLoadStartResult(0, 0, vertexCount, edgesToLoad.Count, startResult.LoadId);
        }
        catch (Exception ex)
        {
            if (reservedJob is not null)
            {
                try
                {
                    reservedJob.Status = "failed";
                    reservedJob.CompletedAt = DateTimeOffset.UtcNow;
                    reservedJob.ErrorSummary = ex.Message;
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    // Best-effort cleanup — the workflow-level failure below is the source of
                    // truth; a double-failure here just leaves the job row "starting" for manual
                    // triage instead of unblocking a future retry.
                }
            }
            await workflowLifecycle.MarkFailedAsync(scanManifestId, WorkflowTypes.Ingestion, ex.Message, runId, IngestionStatuses.GraphLoadFailed, cancellationToken);
            throw;
        }
    }

    /// <summary>Atomic insert-if-absent for the job reservation — raw SQL so a losing concurrent insert is rejected by the DB, not left as a poisoned tracked entity (see Promoter.cs for the same upsert idiom).</summary>
    private static async Task<Guid?> InsertJobIfAbsentAsync(TenantDbContext db, GraphBulkLoadJob job, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO tenant.graph_bulk_load_job
                (id, scan_id, scan_manifest_id, load_id, s3_uri, status, started_at, pending_vertex_deletes, pending_edge_deletes)
            VALUES
                (@id, @scan_id, @scan_manifest_id, @load_id, @s3_uri, @status, @started_at, @pending_vertex_deletes, @pending_edge_deletes)
            ON CONFLICT DO NOTHING
            RETURNING id
            """;

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter("id", job.Id));
            command.Parameters.Add(new NpgsqlParameter("scan_id", job.ScanId));
            command.Parameters.Add(new NpgsqlParameter("scan_manifest_id", job.ScanManifestId));
            command.Parameters.Add(new NpgsqlParameter("load_id", (object?)job.LoadId ?? DBNull.Value));
            command.Parameters.Add(new NpgsqlParameter("s3_uri", job.S3Uri));
            command.Parameters.Add(new NpgsqlParameter("status", job.Status));
            command.Parameters.Add(new NpgsqlParameter("started_at", job.StartedAt));
            command.Parameters.Add(new NpgsqlParameter("pending_vertex_deletes", (object?)job.PendingVertexDeletes ?? DBNull.Value));
            command.Parameters.Add(new NpgsqlParameter("pending_edge_deletes", (object?)job.PendingEdgeDeletes ?? DBNull.Value));

            return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private Task UploadVertexCsvAsync(
        Guid tenantId, string entityType, IReadOnlyList<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> vertices, string key,
        CancellationToken cancellationToken)
    {
        var csv = GremlinCsvWriter.WriteVertexCsv(tenantId, entityType, vertices);
        return _s3.PutObjectAsync(_bucket, key, Encoding.UTF8.GetBytes(csv), cancellationToken);
    }

    private Task UploadEdgeCsvAsync(
        Guid tenantId, IReadOnlyList<(Guid EdgeId, string RelType, GraphVertexRef From, GraphVertexRef To, IReadOnlyDictionary<string, object?> Properties)> edges, string key,
        CancellationToken cancellationToken)
    {
        var csv = GremlinCsvWriter.WriteEdgeCsv(tenantId, edges);
        return _s3.PutObjectAsync(_bucket, key, Encoding.UTF8.GetBytes(csv), cancellationToken);
    }

    private async Task<(List<PendingVertexDelete> ToDelete, List<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> ToLoad)> PartitionAccountsAsync(
        TenantDbContext db, Guid scanId, Guid scanManifestId, CancellationToken cancellationToken)
    {
        var changedIds = await ChangedEntityIdsAsync(db, scanId, scanManifestId, "account", cancellationToken);
        if (changedIds.Count == 0)
        {
            return ([], []);
        }

        var toLoad = new List<(Guid, IReadOnlyDictionary<string, object?>)>();
        var toDelete = new List<PendingVertexDelete>();
        foreach (var account in await db.Accounts.Where(a => changedIds.Contains(a.Id)).ToListAsync(cancellationToken))
        {
            if (account.IsDeleted)
            {
                toDelete.Add(new PendingVertexDelete("account", account.Id));
            }
            else
            {
                toLoad.Add((account.Id, VertexProperties(account)));
            }
        }

        return (toDelete, toLoad);
    }

    private async Task<(List<PendingVertexDelete> ToDelete, List<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> ToLoad)> PartitionGroupsAsync(
        TenantDbContext db, Guid scanId, Guid scanManifestId, CancellationToken cancellationToken)
    {
        var changedIds = await ChangedEntityIdsAsync(db, scanId, scanManifestId, "grp", cancellationToken);
        if (changedIds.Count == 0)
        {
            return ([], []);
        }

        var toLoad = new List<(Guid, IReadOnlyDictionary<string, object?>)>();
        var toDelete = new List<PendingVertexDelete>();
        foreach (var group in await db.Grps.Where(g => changedIds.Contains(g.Id)).ToListAsync(cancellationToken))
        {
            if (group.IsDeleted)
            {
                toDelete.Add(new PendingVertexDelete("grp", group.Id));
            }
            else
            {
                toLoad.Add((group.Id, VertexProperties(group)));
            }
        }

        return (toDelete, toLoad);
    }

    private async Task<List<(Guid Id, IReadOnlyDictionary<string, object?> Properties)>> PartitionAssetsAsync(
        TenantDbContext db, Guid scanId, Guid scanManifestId, CancellationToken cancellationToken)
    {
        var changedIds = await ChangedEntityIdsAsync(db, scanId, scanManifestId, "asset", cancellationToken);
        if (changedIds.Count == 0)
        {
            return [];
        }

        var toLoad = new List<(Guid, IReadOnlyDictionary<string, object?>)>();
        foreach (var asset in await db.Assets.Where(a => changedIds.Contains(a.Id)).ToListAsync(cancellationToken))
        {
            toLoad.Add((asset.Id, VertexProperties(asset)));
        }

        return toLoad;
    }

    private async Task<(List<PendingEdgeDelete> ToDelete, List<(Guid EdgeId, string RelType, GraphVertexRef From, GraphVertexRef To, IReadOnlyDictionary<string, object?> Properties)> ToLoad)> PartitionEdgesAsync(
        TenantDbContext db, Guid scanId, Guid gateManifestId, CancellationToken cancellationToken)
    {
        var events = await db.IngestChangeEvents
            .Where(e => e.ScanId == scanId && e.ScanManifestId == gateManifestId && e.EntityType == "edge")
            .ToListAsync(cancellationToken);
        if (events.Count == 0)
        {
            return ([], []);
        }

        var edgeIds = events.Select(e => e.EntityId).Distinct().ToList();
        var edges = (await db.Edges.Where(e => edgeIds.Contains(e.Id)).ToListAsync(cancellationToken)).ToDictionary(e => e.Id);

        var toLoad = new List<(Guid, string, GraphVertexRef, GraphVertexRef, IReadOnlyDictionary<string, object?>)>();
        var toDelete = new List<PendingEdgeDelete>();
        foreach (var changeEvent in events)
        {
            if (!edges.TryGetValue(changeEvent.EntityId, out var edge))
            {
                continue;
            }

            if (changeEvent.ChangeType == "deleted")
            {
                toDelete.Add(new PendingEdgeDelete(edge.RelType, edge.FromType, edge.FromId, edge.ToType, edge.ToId));
            }
            else
            {
                var from = new GraphVertexRef(edge.FromType, edge.FromId);
                var to = new GraphVertexRef(edge.ToType, edge.ToId);
                toLoad.Add((edge.Id, edge.RelType, from, to, EdgePropsConverter.ToPropertyDictionary(edge.Props)));
            }
        }

        return (toDelete, toLoad);
    }

    private static async Task<IReadOnlyList<Guid>> ChangedEntityIdsAsync(TenantDbContext db, Guid scanId, Guid scanManifestId, string entityType, CancellationToken cancellationToken) =>
        await db.IngestChangeEvents
            .Where(e => e.ScanId == scanId && e.ScanManifestId == scanManifestId && e.EntityType == entityType)
            .Select(e => e.EntityId).Distinct().ToListAsync(cancellationToken);

    private static IReadOnlyDictionary<string, object?> VertexProperties(Account account) => new Dictionary<string, object?>
    {
        ["nativeId"] = account.NativeId,
        ["connectorType"] = account.ConnectorType,
        ["accountKind"] = account.AccountKind,
        ["isHuman"] = account.IsHuman,
        ["displayName"] = account.DisplayName,
        ["samAccountName"] = account.SamAccountName,
        ["upn"] = account.Upn,
        ["email"] = account.Email,
        ["domainName"] = account.DomainName,
        ["isDeleted"] = account.IsDeleted,
        ["isDisabled"] = account.IsDisabled,
    };

    private static IReadOnlyDictionary<string, object?> VertexProperties(Grp grp) => new Dictionary<string, object?>
    {
        ["nativeId"] = grp.NativeId,
        ["connectorType"] = grp.ConnectorType,
        ["groupClass"] = grp.GroupClass,
        ["displayName"] = grp.DisplayName,
        ["email"] = grp.Email,
        ["domainName"] = grp.DomainName,
        ["isLargeGroup"] = grp.IsLargeGroup,
        ["isDeleted"] = grp.IsDeleted,
    };

    private static IReadOnlyDictionary<string, object?> VertexProperties(Asset asset) => new Dictionary<string, object?>
    {
        ["nativeId"] = asset.NativeId,
        ["connectorType"] = asset.ConnectorType,
        ["assetType"] = asset.AssetType,
        ["displayName"] = asset.DisplayName,
        ["fullPath"] = asset.FullPath,
        ["filerName"] = asset.FilerName,
        ["fileSize"] = asset.FileSize,
        ["fileCount"] = asset.FileCount,
        ["brokenAcl"] = asset.BrokenAcl,
        ["isProtected"] = asset.IsProtected,
    };
}
