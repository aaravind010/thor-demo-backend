using System.Text;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Graph;
using Thor.Graph.BulkLoad;
using Thor.S3;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ownership.Constants;
using Thor.Workflows.Ownership.Persistence;

namespace Thor.Workflows.Ownership.Steps;

public sealed record OwnershipGraphLoadStartResult(int IdentitiesQueuedForLoad, int EdgesQueuedForLoad, string? LoadId);

/// <summary>
/// First half of Ownership's graph sync: reads this run's rank-1 <see cref="PartyAssignment"/> rows
/// across all three phases, writes an <c>identity</c> vertex CSV and an <c>OWNED_BY</c> edge CSV —
/// one edge per winning entity — to Ownership's own graph-load bucket, and starts a Neptune bulk
/// load. <see cref="OwnershipGraphLoadPollStep"/> is the second half.
///
/// <para>Postgres stays the system of record: the edges are a projection of <c>party_assignment</c>
/// for traversal queries, written after the run has decided rather than read back by it. The
/// account/group/asset vertices they point at are Ingestion's — its own bulk load finished before it
/// started this run.</para>
///
/// <para>Tracks the load in <see cref="GraphBulkLoadJob"/> keyed by this run's id in both
/// <c>ScanId</c> and <c>ScanManifestId</c>. Neither column has a foreign key, and a run id never
/// equals a scan's id, so Ownership's jobs can never collide with Ingestion's under the unique
/// (ScanId, ScanManifestId) index — and it needs no new column on a table existing tenants already
/// have. The reservation follows Ingestion's <c>GraphLoadStartStep</c>: a <c>starting</c> row is
/// inserted before Neptune is called, so a retry after a crash cannot start a second load of the same
/// data, and a reservation older than the stale threshold is reclaimed.</para>
/// </summary>
public sealed class OwnershipGraphLoadStartStep : WorkflowStep<OwnershipRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;
    private readonly IS3ObjectStore _s3;
    private readonly INeptuneBulkLoaderClient _bulkLoader;
    private readonly string _bucket;
    private readonly string _iamRoleArn;
    private readonly string _region;
    private readonly TimeSpan _startStaleThreshold;

    /// <summary>Test seam — lets tests substitute fakes for every external dependency the parameterless constructor builds for real.</summary>
    public OwnershipGraphLoadStartStep(
        ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager,
        IS3ObjectStore s3, INeptuneBulkLoaderClient bulkLoader, string bucket, string iamRoleArn, string region,
        TimeSpan startStaleThreshold)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
        _s3 = s3;
        _bulkLoader = bulkLoader;
        _bucket = bucket;
        _iamRoleArn = iamRoleArn;
        _region = region;
        _startStaleThreshold = startStaleThreshold;
    }

    public OwnershipGraphLoadStartStep()
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
        _s3 = new S3ObjectStore(new AmazonS3Client());
        _bulkLoader = new NeptuneBulkLoaderClient(neptuneOptions, _loggerFactory);
        _bucket = Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_BUCKET")
            ?? throw new InvalidOperationException("THOR_GRAPH_BULKLOAD_BUCKET is required.");
        _iamRoleArn = Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN")
            ?? throw new InvalidOperationException("THOR_GRAPH_BULKLOAD_IAM_ROLE_ARN is required.");
        _startStaleThreshold = TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_START_STALE_SECONDS"), out var staleSeconds) ? staleSeconds : 300);
    }

    protected override async Task<StepResult> ExecuteAsync(OwnershipRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        return StepResult.Completed(await RunAsync(db, request, cancellationToken));
    }

    public async Task<OwnershipGraphLoadStartResult> RunAsync(TenantDbContext db, OwnershipRequest request, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<OwnershipGraphLoadStartStep>();
        var runId = OwnershipRunIdentity.Derive(request.TenantId, request.ScanManifestId, request.RunId, logger);

        var existing = await PendingJobs(db, runId).OrderByDescending(j => j.StartedAt).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            var isStaleReservation = existing.Status == "starting" && DateTimeOffset.UtcNow - existing.StartedAt > _startStaleThreshold;
            if (!isStaleReservation)
            {
                logger.LogInformation(
                    "Ownership run {RunId} already has a pending bulk load ({Status}) — not starting another.", runId, existing.Status);
                return new OwnershipGraphLoadStartResult(0, 0, existing.LoadId);
            }

            // The reservation never reached Neptune (no LoadId): the invocation that made it was
            // killed before it could. Reclaim it so this attempt can start a fresh load.
            logger.LogWarning(
                "Ownership run {RunId} had a stale 'starting' bulk load job {JobId} (reserved at {StartedAt}) — marking it failed and starting a fresh attempt.",
                runId, existing.Id, existing.StartedAt);
            existing.Status = "failed";
            existing.CompletedAt = DateTimeOffset.UtcNow;
            existing.ErrorSummary = "Abandoned: exceeded starting-phase staleness threshold without reaching Neptune.";
            await db.SaveChangesAsync(cancellationToken);
        }

        var runIdText = runId.ToString();
        var winners = await db.PartyAssignments
            .AsNoTracking()
            .Where(pa => pa.RunId == runIdText && pa.Rank == 1 && pa.IsActive
                && ScopeConstants.EntityTypesInPhaseOrder.Contains(pa.EntityType))
            .Select(pa => new { pa.Id, pa.EntityType, pa.EntityId, pa.IdentityId })
            .ToListAsync(cancellationToken);

        if (winners.Count == 0)
        {
            logger.LogInformation("Ownership run {RunId}: no rank-1 winners — nothing to bulk-load.", runId);
            return new OwnershipGraphLoadStartResult(0, 0, null);
        }

        var identityIds = winners.Select(w => w.IdentityId).Distinct().ToList();
        var identities = await db.Identities
            .AsNoTracking()
            .Where(i => identityIds.Contains(i.Id))
            .Select(i => new { i.Id, i.DisplayName, i.Email })
            .ToListAsync(cancellationToken);

        var identityVertices = identities
            .Select(i => (i.Id, (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["displayName"] = i.DisplayName,
                ["email"] = i.Email,
            }))
            .ToList();

        var ownedByEdges = winners
            .Select(w => (
                EdgeId: w.Id,
                RelType: ScopeConstants.OwnedByRelType,
                From: new GraphVertexRef(w.EntityType, w.EntityId),
                To: new GraphVertexRef(ScopeConstants.IdentityVertexType, w.IdentityId),
                Properties: (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>()))
            .ToList();

        var attemptId = Guid.NewGuid();
        var s3Prefix = $"graph-bulk-load/{request.TenantId:N}/ownership/{runId:N}/{attemptId:N}";
        var s3Uri = new Uri($"s3://{_bucket}/{s3Prefix}/");

        var reservedJob = new GraphBulkLoadJob
        {
            Id = attemptId,
            ScanId = runId,
            ScanManifestId = runId,
            LoadId = null,
            S3Uri = s3Uri.ToString(),
            Status = "starting",
            StartedAt = DateTimeOffset.UtcNow,
        };

        if (!await InsertJobIfAbsentAsync(db, reservedJob, cancellationToken))
        {
            // Lost the race to a concurrent invocation for this run; its row is the pending job.
            var winner = await PendingJobs(db, runId).OrderByDescending(j => j.StartedAt).FirstOrDefaultAsync(cancellationToken);
            logger.LogInformation("Ownership run {RunId} already has a pending bulk load (lost reservation race) — not starting another.", runId);
            return new OwnershipGraphLoadStartResult(0, 0, winner?.LoadId);
        }

        db.GraphBulkLoadJobs.Attach(reservedJob);

        try
        {
            await Task.WhenAll(
                UploadAsync(GremlinCsvWriter.WriteVertexCsv(request.TenantId, ScopeConstants.IdentityVertexType, identityVertices),
                    $"{s3Prefix}/vertices/identities.csv", cancellationToken),
                UploadAsync(GremlinCsvWriter.WriteEdgeCsv(request.TenantId, ownedByEdges),
                    $"{s3Prefix}/edges/owned_by.csv", cancellationToken));

            var startResult = await _bulkLoader.StartLoadAsync(s3Uri, _iamRoleArn, _region, cancellationToken: cancellationToken);

            reservedJob.LoadId = startResult.LoadId;
            reservedJob.Status = "started";
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Ownership run {RunId}: started load {LoadId} ({IdentityCount} identity / {EdgeCount} OWNED_BY row(s)).",
                runId, startResult.LoadId, identityVertices.Count, ownedByEdges.Count);

            return new OwnershipGraphLoadStartResult(identityVertices.Count, ownedByEdges.Count, startResult.LoadId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ownership run {RunId}: starting the bulk load failed.", runId);
            try
            {
                reservedJob.Status = "failed";
                reservedJob.CompletedAt = DateTimeOffset.UtcNow;
                reservedJob.ErrorSummary = ex.Message;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception cleanupEx)
            {
                // Best effort: the run is failing either way. A job left "starting" is reclaimed by
                // the next attempt once it passes the stale threshold.
                logger.LogWarning(cleanupEx, "Ownership run {RunId}: could not mark bulk load job {JobId} failed.", runId, reservedJob.Id);
            }

            throw;
        }
    }

    private static IQueryable<GraphBulkLoadJob> PendingJobs(TenantDbContext db, Guid runId) =>
        db.GraphBulkLoadJobs.Where(j => j.ScanId == runId && j.ScanManifestId == runId && (j.Status == "starting" || j.Status == "started"));

    private Task UploadAsync(string csv, string key, CancellationToken cancellationToken) =>
        _s3.PutObjectAsync(_bucket, key, Encoding.UTF8.GetBytes(csv), cancellationToken);

    /// <summary>
    /// Atomic insert-if-absent for the reservation, as Ingestion's <c>GraphLoadStartStep</c> does it:
    /// raw SQL so a losing concurrent insert is rejected by the unique index rather than left as a
    /// poisoned tracked entity.
    /// </summary>
    private static async Task<bool> InsertJobIfAbsentAsync(TenantDbContext db, GraphBulkLoadJob job, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO tenant.graph_bulk_load_job (id, scan_id, scan_manifest_id, load_id, s3_uri, status, started_at)
            VALUES (@id, @scan_id, @scan_manifest_id, NULL, @s3_uri, @status, @started_at)
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
            command.Parameters.Add(new NpgsqlParameter("s3_uri", job.S3Uri));
            command.Parameters.Add(new NpgsqlParameter("status", job.Status));
            command.Parameters.Add(new NpgsqlParameter("started_at", job.StartedAt));

            return await command.ExecuteScalarAsync(cancellationToken) is Guid;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
