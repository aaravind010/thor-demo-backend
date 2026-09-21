using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Graph;
using Thor.Graph.BulkLoad;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Ingestion.Composition;
using Thor.Workflows.Ingestion.Constants;

namespace Thor.Workflows.Ingestion.Steps;

public sealed record GraphLoadPollResult(string Outcome, string? LoadId, Guid WorkflowId, int VerticesDeleted = 0, int EdgesDeleted = 0)
{
    public bool IsInProgress => Outcome == "in-progress";
}

/// <summary>
/// Second half of the graph load: checks Neptune once per invocation for the bulk load job
/// <see cref="GraphLoadStartStep"/> started for this scan. Returns a result whose
/// <see cref="GraphLoadPollResult.Outcome"/>/<see cref="GraphLoadPollResult.IsInProgress"/>
/// tells the caller whether the load is done, still running (wait and re-invoke this step
/// later), or throws on failure — so Step Functions' own wait/retry/catch decides what happens
/// next, rather than this step blocking until the load finishes.
/// </summary>
public sealed class GraphLoadPollStep : IWorkflowStep
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;
    private readonly IGraphVertexStore _vertexStore;
    private readonly IGraphEdgeStore _edgeStore;
    private readonly INeptuneBulkLoaderClient _bulkLoader;

    /// <summary>Test seam — lets tests substitute fakes instead of the parameterless constructor's real ones.</summary>
    public GraphLoadPollStep(
        ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager,
        IGraphVertexStore vertexStore, IGraphEdgeStore edgeStore,
        INeptuneBulkLoaderClient bulkLoader)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
        _vertexStore = vertexStore;
        _edgeStore = edgeStore;
        _bulkLoader = bulkLoader;
    }

    public GraphLoadPollStep()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _tenantConnectionManager = TenantConnectionManagerFactory.Build();

        var region = Environment.GetEnvironmentVariable("THOR_AWS_REGION") ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? throw new InvalidOperationException("THOR_AWS_REGION or AWS_REGION is required.");
        var neptuneOptions = new NeptuneOptions
        {
            Endpoint = Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENDPOINT") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_PORT"), out var neptunePort) ? neptunePort : 8182,
            EnableSsl = bool.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENABLESSL"), out var neptuneSsl) ? neptuneSsl : true,
            Region = region,
        };
        _vertexStore = new GraphVertexStore(neptuneOptions);
        _edgeStore = new GraphEdgeStore(neptuneOptions);
        _bulkLoader = new NeptuneBulkLoaderClient(neptuneOptions);
    }

    public async Task<(object? Result, bool IsInProgress)> ExecuteAsync(string inputJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<IngestionRequest>(inputJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{WorkflowHost.InputEnvVar} did not deserialize to a valid IngestionRequest.");

        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        var result = await RunAsync(db, request.TenantId, request.ScanId, request.ScanManifestId, request.RunId, cancellationToken);
        return (result, result.IsInProgress);
    }

    public async Task<GraphLoadPollResult> RunAsync(
        TenantDbContext db, Guid tenantId, Guid scanId, Guid scanManifestId, Guid? runId = null, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<GraphLoadPollStep>();

        var workflowLifecycle = new WorkflowLifecycle(new WorkflowRepository(db));
        var workflowId = await workflowLifecycle.EnsureStartedAsync(scanId, scanManifestId, WorkflowTypes.Ingestion, WorkflowTriggers.StepFunctions, runId, cancellationToken);

        try
        {
            var job = await db.GraphBulkLoadJobs
                .Where(j => j.ScanId == scanId && j.ScanManifestId == scanManifestId && j.Status == "started")
                .OrderByDescending(j => j.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (job is null)
            {
                logger.LogInformation("No pending bulk load job for scan {ScanId} — nothing to poll.", scanId);
                await workflowLifecycle.MarkCompletedAsync(scanManifestId, WorkflowTypes.Ingestion, runId, cancellationToken);
                return new GraphLoadPollResult("no-pending-job", null, workflowId);
            }

            var loadId = job.LoadId
                ?? throw new InvalidOperationException($"GraphBulkLoadJob {job.Id} has status 'started' but no LoadId.");

            var status = await _bulkLoader.GetLoadStatusAsync(loadId, cancellationToken);

            if (status.Status == BulkLoadStatus.Completed && !status.HasRowErrors)
            {
                // Deletes for this job were snapshotted (not recomputed) at graph-load-start
                // time and deferred until now — applying them only once the load they were
                // gated on is confirmed complete means a failed load never applies them,
                // leaving the graph in its pre-run, recoverable state.
                var verticesDeleted = await GraphDeleteExecutor.DeleteVerticesAsync(
                    _vertexStore, tenantId, DeserializePendingVertexDeletes(job.PendingVertexDeletes), cancellationToken);
                var edgesDeleted = await GraphDeleteExecutor.DeleteEdgesAsync(
                    _edgeStore, tenantId, DeserializePendingEdgeDeletes(job.PendingEdgeDeletes), cancellationToken);

                job.Status = "completed";
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.PendingVertexDeletes = null;
                job.PendingEdgeDeletes = null;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Bulk load {LoadId} for scan {ScanId} completed ({TotalRecords} record(s)), {VerticesDeleted} vertex delete(s)/{EdgesDeleted} edge delete(s) applied.",
                    loadId, scanId, status.TotalRecords, verticesDeleted, edgesDeleted);
                await workflowLifecycle.MarkCompletedAsync(scanManifestId, WorkflowTypes.Ingestion, runId, cancellationToken);
                return new GraphLoadPollResult("completed", loadId, workflowId, verticesDeleted, edgesDeleted);
            }

            if (status.Status == BulkLoadStatus.Failed || status.HasRowErrors)
            {
                var summary = status.HasRowErrors
                    ? $"Completed with row errors: {status.ParsingErrors} parsing, {status.DatatypeMismatchErrors} datatype, {status.InsertErrors} insert."
                    : $"Status {status.RawStatus}: {string.Join("; ", status.ErrorMessages)}";
                job.Status = "failed";
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.ErrorSummary = summary;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogError("Bulk load {LoadId} for scan {ScanId} failed: {Summary}", loadId, scanId, summary);
                throw new InvalidOperationException($"Neptune bulk load {loadId} failed: {summary}");
            }

            // Still running: leave the job/workflow rows in their in-flight state (not
            // completed, not failed) and report "in-progress" via IsInProgress so the caller —
            // Step Functions, via the ECS exit code or the raw Lambda JSON output — waits and
            // re-invokes this step later instead of treating this invocation as done or failed.
            logger.LogInformation("Bulk load {LoadId} for scan {ScanId} is still in progress.", loadId, scanId);
            return new GraphLoadPollResult("in-progress", loadId, workflowId);
        }
        catch (Exception ex)
        {
            await workflowLifecycle.MarkFailedAsync(scanManifestId, WorkflowTypes.Ingestion, ex.Message, runId, IngestionStatuses.GraphLoadFailed, cancellationToken);
            throw;
        }
    }

    private static List<PendingVertexDelete> DeserializePendingVertexDeletes(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<PendingVertexDelete>>(json) ?? [];

    private static List<PendingEdgeDelete> DeserializePendingEdgeDeletes(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<PendingEdgeDelete>>(json) ?? [];
}
