using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;
using Thor.Graph;
using Thor.Graph.BulkLoad;
using Thor.Workflows.Abstractions;
using Thor.Workflows.Hosting.Composition;
using Thor.Workflows.Ownership.Persistence;

namespace Thor.Workflows.Ownership.Steps;

public sealed record OwnershipGraphLoadPollResult(string Outcome, string? LoadId)
{
    public bool IsInProgress => Outcome == "in-progress";
}

/// <summary>
/// Second half of Ownership's graph sync: checks Neptune once per invocation for the load
/// <see cref="OwnershipGraphLoadStartStep"/> started for this run. Still running answers
/// <see cref="OwnershipGraphLoadPollResult.IsInProgress"/> and the state machine waits and asks again,
/// as it does for Ingestion's <c>GraphLoadPollStep</c>; a failed load, or one that completed with
/// row errors, throws to the Catch path.
///
/// <para>Leaves the <c>workflow</c> row alone either way. Unlike Ingestion's poll it is not the last
/// step — <see cref="OwnershipFinalizeStep"/> closes the row, and the Catch path's
/// <see cref="OwnershipRecordFailureStep"/> records a failure.</para>
/// </summary>
public sealed class OwnershipGraphLoadPollStep : WorkflowStep<OwnershipRequest>
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITenantConnectionManager _tenantConnectionManager;
    private readonly INeptuneBulkLoaderClient _bulkLoader;

    /// <summary>Test seam — lets tests substitute fakes instead of the parameterless constructor's real ones.</summary>
    public OwnershipGraphLoadPollStep(
        ILoggerFactory loggerFactory, ITenantConnectionManager tenantConnectionManager, INeptuneBulkLoaderClient bulkLoader)
    {
        _loggerFactory = loggerFactory;
        _tenantConnectionManager = tenantConnectionManager;
        _bulkLoader = bulkLoader;
    }

    public OwnershipGraphLoadPollStep()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _tenantConnectionManager = TenantConnectionManagerFactory.Build();

        var region = Environment.GetEnvironmentVariable("THOR_AWS_REGION") ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? throw new InvalidOperationException("THOR_AWS_REGION or AWS_REGION is required.");
        _bulkLoader = new NeptuneBulkLoaderClient(new NeptuneOptions
        {
            Endpoint = Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENDPOINT") ?? "localhost",
            Port = int.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_PORT"), out var neptunePort) ? neptunePort : 8182,
            EnableSsl = bool.TryParse(Environment.GetEnvironmentVariable("THOR_NEPTUNE_ENABLESSL"), out var neptuneSsl) ? neptuneSsl : true,
            Region = region,
        }, _loggerFactory);
    }

    protected override async Task<StepResult> ExecuteAsync(OwnershipRequest request, CancellationToken cancellationToken)
    {
        await using var db = await _tenantConnectionManager.GetTenantDbContextAsync(request.TenantId, cancellationToken);
        var result = await RunAsync(db, request, cancellationToken);

        // IsInProgress rides on the result as well as the envelope: the state machine's Choice reads
        // it straight off the Lambda response.
        return result.IsInProgress ? StepResult.InProgress(result) : StepResult.Completed(result);
    }

    public async Task<OwnershipGraphLoadPollResult> RunAsync(TenantDbContext db, OwnershipRequest request, CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<OwnershipGraphLoadPollStep>();
        var runId = OwnershipRunIdentity.Derive(request.TenantId, request.ScanManifestId, request.RunId, logger);

        var job = await db.GraphBulkLoadJobs
            .Where(j => j.ScanId == runId && j.ScanManifestId == runId)
            .OrderByDescending(j => j.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is { Status: "failed" })
        {
            // A retried poll must not mistake an already-failed load for "nothing to load".
            throw new InvalidOperationException($"Neptune bulk load job {job.Id} failed: {job.ErrorSummary}");
        }

        if (job is not { Status: "started" })
        {
            // graph-load-start found nothing to load.
            logger.LogInformation("No pending bulk load for Ownership run {RunId} — nothing to poll.", runId);
            return new OwnershipGraphLoadPollResult("no-pending-job", null);
        }

        var loadId = job.LoadId
            ?? throw new InvalidOperationException($"GraphBulkLoadJob {job.Id} has status 'started' but no LoadId.");

        var status = await _bulkLoader.GetLoadStatusAsync(loadId, cancellationToken);

        if (status.Status == BulkLoadStatus.Completed && !status.HasRowErrors)
        {
            job.Status = "completed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Bulk load {LoadId} for Ownership run {RunId} completed ({TotalRecords} record(s)).", loadId, runId, status.TotalRecords);
            return new OwnershipGraphLoadPollResult("completed", loadId);
        }

        if (status.Status == BulkLoadStatus.Failed || status.HasRowErrors)
        {
            var summary = status.HasRowErrors
                ? $"Completed with row errors: {status.ParsingErrors} parsing, {status.DatatypeMismatchErrors} datatype, {status.InsertErrors} insert. Sample errors: {string.Join("; ", status.ErrorMessages.Take(5))}"
                : $"Status {status.RawStatus}: {string.Join("; ", status.ErrorMessages)}";
            job.Status = "failed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorSummary = summary;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError("Bulk load {LoadId} for Ownership run {RunId} failed: {Summary}", loadId, runId, summary);
            throw new InvalidOperationException($"Neptune bulk load {loadId} failed: {summary}");
        }

        logger.LogInformation("Bulk load {LoadId} for Ownership run {RunId} is still in progress.", loadId, runId);
        return new OwnershipGraphLoadPollResult("in-progress", loadId);
    }
}
