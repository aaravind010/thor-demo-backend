using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.Graph;
using Thor.Graph.BulkLoad;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Ingestion.Composition;

namespace Thor.Workflows.Ingestion.Steps;

public sealed record GraphLoadPollResult(string Outcome, string? LoadId);

/// <summary>
/// Second half of the graph load: polls Neptune for the bulk load job <see cref="GraphLoadStartStep"/>
/// started for this scan, bounded by an in-container poll loop (per the ingestion redesign
/// plan — the wait lives in this container, not in Step Functions). Exits non-zero on failure
/// or on exceeding the wait bound, so Step Functions' own retry/catch decides whether to
/// re-invoke this step again later or escalate to DLQ.
/// </summary>
public sealed class GraphLoadPollStep : IWorkflowStep
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;
    private readonly INeptuneBulkLoaderClient _bulkLoader;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _maxWait;

    /// <summary>Test seam — lets tests substitute fakes and short poll/wait durations instead of the parameterless constructor's real ones.</summary>
    public GraphLoadPollStep(
        ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager,
        INeptuneBulkLoaderClient bulkLoader, TimeSpan pollInterval, TimeSpan maxWait)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
        _bulkLoader = bulkLoader;
        _pollInterval = pollInterval;
        _maxWait = maxWait;
    }

    public GraphLoadPollStep()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _tenantConnectionManager = TenantConnectionManagerFactory.Build();

        var neptuneOptions = new NeptuneOptions
        {
            Endpoint = Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENDPOINT") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_PORT"), out var neptunePort) ? neptunePort : 8182,
            EnableSsl = bool.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENABLESSL"), out var neptuneSsl) ? neptuneSsl : true,
        };
        _bulkLoader = new NeptuneBulkLoaderClient(neptuneOptions);
        _pollInterval = TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_POLL_INTERVAL_SECONDS"), out var interval) ? interval : 10);
        _maxWait = TimeSpan.FromSeconds(
            int.TryParse(Environment.GetEnvironmentVariable("THOR_GRAPH_BULKLOAD_MAX_WAIT_SECONDS"), out var maxWaitSeconds) ? maxWaitSeconds : 300);
    }

    public async Task ExecuteAsync(string inputJson, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<IngestionRequest>(inputJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{WorkflowHost.InputEnvVar} did not deserialize to a valid IngestionRequest.");

        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId);
        await RunAsync(db, request.ScanId, cancellationToken);
    }

    public async Task<GraphLoadPollResult> RunAsync(TenantDbContext db, Guid scanId, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<GraphLoadPollStep>();

        var job = await db.GraphBulkLoadJobs
            .Where(j => j.ScanId == scanId && j.Status == "started")
            .OrderByDescending(j => j.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            logger.LogInformation("No pending bulk load job for scan {ScanId} — nothing to poll.", scanId);
            return new GraphLoadPollResult("no-pending-job", null);
        }

        var deadline = DateTimeOffset.UtcNow + _maxWait;
        var currentInterval = _pollInterval;
        var intervalCap = _pollInterval * 6;
        while (true)
        {
            var status = await _bulkLoader.GetLoadStatusAsync(job.LoadId, cancellationToken);

            if (status.Status == BulkLoadStatus.Completed && !status.HasRowErrors)
            {
                job.Status = "completed";
                job.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Bulk load {LoadId} for scan {ScanId} completed ({TotalRecords} record(s)).", job.LoadId, scanId, status.TotalRecords);
                return new GraphLoadPollResult("completed", job.LoadId);
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
                logger.LogError("Bulk load {LoadId} for scan {ScanId} failed: {Summary}", job.LoadId, scanId, summary);
                throw new InvalidOperationException($"Neptune bulk load {job.LoadId} failed: {summary}");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogWarning(
                    "Bulk load {LoadId} for scan {ScanId} did not reach a terminal state within {MaxWait} — " +
                    "exiting non-zero for Step Functions to retry this step.", job.LoadId, scanId, _maxWait);
                throw new TimeoutException($"Neptune bulk load {job.LoadId} did not reach a terminal state within {_maxWait}.");
            }

            var jitterMs = Random.Shared.Next(0, (int)(currentInterval.TotalMilliseconds * 0.2) + 1);
            await Task.Delay(currentInterval + TimeSpan.FromMilliseconds(jitterMs), cancellationToken);
            currentInterval = TimeSpan.FromMilliseconds(Math.Min(currentInterval.TotalMilliseconds * 2, intervalCap.TotalMilliseconds));
        }
    }
}
