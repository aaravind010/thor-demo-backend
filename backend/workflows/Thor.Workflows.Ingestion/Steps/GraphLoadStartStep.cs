using System.Text;
using System.Text.Json;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    /// <summary>Test seam — lets tests substitute fakes for every external dependency the parameterless constructor builds for real.</summary>
    public GraphLoadStartStep(
        ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager,
        IGraphVertexStore vertexStore, IGraphEdgeStore edgeStore, IS3ObjectStore s3,
        INeptuneBulkLoaderClient bulkLoader, string bucket, string iamRoleArn, string region)
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
    }

    public GraphLoadStartStep()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _tenantConnectionManager = TenantConnectionManagerFactory.Build();

        var neptuneOptions = new NeptuneOptions
        {
            Endpoint = Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENDPOINT") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_PORT"), out var neptunePort) ? neptunePort : 8182,
            EnableSsl = bool.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENABLESSL"), out var neptuneSsl) ? neptuneSsl : true,
        };
        _vertexStore = new GraphVertexStore(neptuneOptions);
        _edgeStore = new GraphEdgeStore(neptuneOptions);
        _s3 = new S3ObjectStore(new AmazonS3Client());
        _bulkLoader = new NeptuneBulkLoaderClient(neptuneOptions);
        _bucket = Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_BUCKET")
            ?? throw new InvalidOperationException("THOR_GRAPH_BULKLOAD_BUCKET is required.");
        _iamRoleArn = Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN")
            ?? throw new InvalidOperationException("THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN is required.");
        _region = Environment.GetEnvironmentVariable("THOR_AWS_REGION") ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? throw new InvalidOperationException("THOR_AWS_REGION or AWS_REGION is required.");
    }

    public async Task ExecuteAsync(string inputJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<IngestionRequest>(inputJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{WorkflowHost.InputEnvVar} did not deserialize to a valid IngestionRequest.");

        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId);
        await RunAsync(db, request.TenantId, request.ScanId, request.ScanManifestId);
    }

    public async Task<GraphLoadStartResult> RunAsync(TenantDbContext db, Guid tenantId, Guid scanId, Guid scanManifestId)
    {
        var logger = _loggerFactory.CreateLogger<GraphLoadStartStep>();

        // Idempotency: a retried invocation for the same scan shouldn't start a second load
        // job while one is still pending (mirrors the tenant+scanId idempotency-key pattern
        // used elsewhere for CDC — see ADR §12).
        var existing = await db.GraphBulkLoadJobs
            .Where(j => j.ScanId == scanId && j.Status == "started")
            .OrderByDescending(j => j.StartedAt)
            .FirstOrDefaultAsync();
        if (existing is not null)
        {
            logger.LogInformation("Scan {ScanId} already has a pending bulk load {LoadId} — not starting another.", scanId, existing.LoadId);
            return new GraphLoadStartResult(0, 0, 0, 0, existing.LoadId);
        }

        var edgeResolver = new EdgeResolver(new OneSidedRefIndex(), _loggerFactory.CreateLogger<EdgeResolver>());
        var scanLifecycle = new ScanLifecycle(new ScanRepository(db), new ScanManifestRepository(db));

        var isInitial = await scanLifecycle.IsInitialScanAsync(scanId);
        _ = isInitial
            ? await edgeResolver.ResolveFullAsync(db, scanId, scanManifestId, RelTypes.TwoSided)
            : await edgeResolver.ResolveIncrementalAsync(db, scanId, scanManifestId, RelTypes.TwoSided);

        var (accountsDeleted, accountsToLoad) = await PartitionAccountsAsync(db, tenantId, scanId, scanManifestId);
        var (groupsDeleted, groupsToLoad) = await PartitionGroupsAsync(db, tenantId, scanId, scanManifestId);
        var assetsToLoad = await PartitionAssetsAsync(db, scanId, scanManifestId);
        var (edgesDeleted, edgesToLoad) = await PartitionEdgesAsync(db, tenantId, scanId, scanManifestId);

        var vertexCount = accountsToLoad.Count + groupsToLoad.Count + assetsToLoad.Count;
        var verticesDeleted = accountsDeleted + groupsDeleted;
        if (vertexCount == 0 && edgesToLoad.Count == 0)
        {
            logger.LogInformation(
                "Graph load start for scan {ScanId}: nothing to bulk-load ({VerticesDeleted} vertex delete(s), {EdgesDeleted} edge delete(s)).",
                scanId, verticesDeleted, edgesDeleted);
            return new GraphLoadStartResult(verticesDeleted, edgesDeleted, 0, 0, null);
        }

        var attemptId = Guid.NewGuid();
        var s3Prefix = $"graph-bulk-load/{tenantId:N}/{scanId:N}/{attemptId:N}";

        var uploadTasks = new List<Task>();
        if (accountsToLoad.Count > 0)
        {
            uploadTasks.Add(UploadVertexCsvAsync(tenantId, "account", accountsToLoad, $"{s3Prefix}/vertices/accounts.csv"));
        }
        if (groupsToLoad.Count > 0)
        {
            uploadTasks.Add(UploadVertexCsvAsync(tenantId, "grp", groupsToLoad, $"{s3Prefix}/vertices/groups.csv"));
        }
        if (assetsToLoad.Count > 0)
        {
            uploadTasks.Add(UploadVertexCsvAsync(tenantId, "asset", assetsToLoad, $"{s3Prefix}/vertices/assets.csv"));
        }
        if (edgesToLoad.Count > 0)
        {
            uploadTasks.Add(UploadEdgeCsvAsync(tenantId, edgesToLoad, $"{s3Prefix}/edges/edges.csv"));
        }
        await Task.WhenAll(uploadTasks);

        var s3Uri = new Uri($"s3://{_bucket}/{s3Prefix}/");
        var startResult = await _bulkLoader.StartLoadAsync(s3Uri, _iamRoleArn, _region);

        db.GraphBulkLoadJobs.Add(new GraphBulkLoadJob
        {
            Id = Guid.NewGuid(),
            ScanId = scanId,
            LoadId = startResult.LoadId,
            S3Uri = s3Uri.ToString(),
            Status = "started",
            StartedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Graph load start for scan {ScanId}: started load {LoadId} ({VertexCount} vertex/{EdgeCount} edge row(s)), " +
            "{VerticesDeleted} vertex delete(s), {EdgesDeleted} edge delete(s).",
            scanId, startResult.LoadId, vertexCount, edgesToLoad.Count, verticesDeleted, edgesDeleted);

        return new GraphLoadStartResult(verticesDeleted, edgesDeleted, vertexCount, edgesToLoad.Count, startResult.LoadId);
    }

    private const int GraphWriteMaxDegreeOfParallelism = 4;

    private Task UploadVertexCsvAsync(
        Guid tenantId, string entityType, IReadOnlyList<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> vertices, string key)
    {
        var csv = GremlinCsvWriter.WriteVertexCsv(tenantId, entityType, vertices);
        return _s3.PutObjectAsync(_bucket, key, Encoding.UTF8.GetBytes(csv));
    }

    private Task UploadEdgeCsvAsync(
        Guid tenantId, IReadOnlyList<(Guid EdgeId, string RelType, GraphVertexRef From, GraphVertexRef To, IReadOnlyDictionary<string, object?> Properties)> edges, string key)
    {
        var csv = GremlinCsvWriter.WriteEdgeCsv(tenantId, edges);
        return _s3.PutObjectAsync(_bucket, key, Encoding.UTF8.GetBytes(csv));
    }

    private async Task<(int Deleted, List<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> ToLoad)> PartitionAccountsAsync(
        TenantDbContext db, Guid tenantId, Guid scanId, Guid scanManifestId)
    {
        var changedIds = await ChangedEntityIdsAsync(db, scanId, scanManifestId, "account");
        if (changedIds.Count == 0)
        {
            return (0, []);
        }

        var toLoad = new List<(Guid, IReadOnlyDictionary<string, object?>)>();
        var toDelete = new List<Guid>();
        foreach (var account in await db.Accounts.Where(a => changedIds.Contains(a.Id)).ToListAsync())
        {
            if (account.IsDeleted)
            {
                toDelete.Add(account.Id);
            }
            else
            {
                toLoad.Add((account.Id, VertexProperties(account)));
            }
        }

        await Parallel.ForEachAsync(toDelete, new ParallelOptions { MaxDegreeOfParallelism = GraphWriteMaxDegreeOfParallelism },
            async (id, _) => await _vertexStore.DeleteVertexAsync(tenantId, "account", id));

        return (toDelete.Count, toLoad);
    }

    private async Task<(int Deleted, List<(Guid Id, IReadOnlyDictionary<string, object?> Properties)> ToLoad)> PartitionGroupsAsync(
        TenantDbContext db, Guid tenantId, Guid scanId, Guid scanManifestId)
    {
        var changedIds = await ChangedEntityIdsAsync(db, scanId, scanManifestId, "grp");
        if (changedIds.Count == 0)
        {
            return (0, []);
        }

        var toLoad = new List<(Guid, IReadOnlyDictionary<string, object?>)>();
        var toDelete = new List<Guid>();
        foreach (var group in await db.Grps.Where(g => changedIds.Contains(g.Id)).ToListAsync())
        {
            if (group.IsDeleted)
            {
                toDelete.Add(group.Id);
            }
            else
            {
                toLoad.Add((group.Id, VertexProperties(group)));
            }
        }

        await Parallel.ForEachAsync(toDelete, new ParallelOptions { MaxDegreeOfParallelism = GraphWriteMaxDegreeOfParallelism },
            async (id, _) => await _vertexStore.DeleteVertexAsync(tenantId, "grp", id));

        return (toDelete.Count, toLoad);
    }

    private async Task<List<(Guid Id, IReadOnlyDictionary<string, object?> Properties)>> PartitionAssetsAsync(
        TenantDbContext db, Guid scanId, Guid scanManifestId)
    {
        var changedIds = await ChangedEntityIdsAsync(db, scanId, scanManifestId, "asset");
        if (changedIds.Count == 0)
        {
            return [];
        }

        var toLoad = new List<(Guid, IReadOnlyDictionary<string, object?>)>();
        foreach (var asset in await db.Assets.Where(a => changedIds.Contains(a.Id)).ToListAsync())
        {
            toLoad.Add((asset.Id, VertexProperties(asset)));
        }

        return toLoad;
    }

    private async Task<(int Deleted, List<(Guid EdgeId, string RelType, GraphVertexRef From, GraphVertexRef To, IReadOnlyDictionary<string, object?> Properties)> ToLoad)> PartitionEdgesAsync(
        TenantDbContext db, Guid tenantId, Guid scanId, Guid gateManifestId)
    {
        var events = await db.IngestChangeEvents
            .Where(e => e.ScanId == scanId && e.ScanManifestId == gateManifestId && e.EntityType == "edge")
            .ToListAsync();
        if (events.Count == 0)
        {
            return (0, []);
        }

        var edgeIds = events.Select(e => e.EntityId).Distinct().ToList();
        var edges = (await db.Edges.Where(e => edgeIds.Contains(e.Id)).ToListAsync()).ToDictionary(e => e.Id);

        var toLoad = new List<(Guid, string, GraphVertexRef, GraphVertexRef, IReadOnlyDictionary<string, object?>)>();
        var toDelete = new List<(string RelType, GraphVertexRef From, GraphVertexRef To)>();
        foreach (var changeEvent in events)
        {
            if (!edges.TryGetValue(changeEvent.EntityId, out var edge))
            {
                continue;
            }

            var from = new GraphVertexRef(edge.FromType, edge.FromId);
            var to = new GraphVertexRef(edge.ToType, edge.ToId);

            if (changeEvent.ChangeType == "deleted")
            {
                toDelete.Add((edge.RelType, from, to));
            }
            else
            {
                toLoad.Add((edge.Id, edge.RelType, from, to, EdgePropsConverter.ToPropertyDictionary(edge.Props)));
            }
        }

        await Parallel.ForEachAsync(toDelete, new ParallelOptions { MaxDegreeOfParallelism = GraphWriteMaxDegreeOfParallelism },
            async (item, _) => await _edgeStore.DeleteEdgeAsync(tenantId, item.RelType, item.From, item.To));

        return (toDelete.Count, toLoad);
    }

    private static async Task<IReadOnlyList<Guid>> ChangedEntityIdsAsync(TenantDbContext db, Guid scanId, Guid scanManifestId, string entityType) =>
        await db.IngestChangeEvents
            .Where(e => e.ScanId == scanId && e.ScanManifestId == scanManifestId && e.EntityType == entityType)
            .Select(e => e.EntityId).Distinct().ToListAsync();

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
