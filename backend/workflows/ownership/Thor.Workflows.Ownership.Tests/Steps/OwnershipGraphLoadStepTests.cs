using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.DataLayer.Models.Tenants;
using Thor.Graph.BulkLoad;
using Thor.Workflows.Ownership.Steps;
using Thor.Workflows.Ownership.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Steps;

/// <summary>
/// Both halves of the OWNED_BY projection against a real Postgres, with S3 and the Neptune loader
/// faked. The job row is keyed by the run's own id, so these also check it never touches a job
/// Ingestion owns.
/// </summary>
public sealed class OwnershipGraphLoadStepTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private const string Bucket = "thor-test-workflow-ownership-graphload";

    private FakeTenantConnectionManager Connections => new(_ => db.NewContext());

    private OwnershipGraphLoadStartStep Start(FakeS3ObjectStore s3, FakeBulkLoaderClient loader) =>
        new(NullLoggerFactory.Instance, Connections, s3, loader, Bucket, "arn:aws:iam::0:role/bulk", "us-east-1", TimeSpan.FromMinutes(5));

    private OwnershipGraphLoadPollStep Poll(FakeBulkLoaderClient loader) => new(NullLoggerFactory.Instance, Connections, loader);

    private async Task<(Guid RunId, Guid AccountId, Guid IdentityId)> SeedWinnerAsync(string nativeId)
    {
        var runId = Guid.NewGuid();
        var identity = await db.AddIdentityAsync($"G-{nativeId}", nativeId, $"{nativeId}@corp.com");
        var account = await db.AddAccountAsync(nativeId, identity.Email);
        db.Context.PartyAssignments.Add(new PartyAssignment
        {
            Id = Guid.NewGuid(), EntityType = "account", EntityId = account.Id, IdentityId = identity.Id, Rank = 1,
            IsActive = true, VoteDistribution = "{}", ContributingRuleIds = [], PrecisionScoreSnapshot = "{}",
            RunId = runId.ToString(), AssignedAt = DateTimeOffset.UtcNow,
        });
        await db.Context.SaveChangesAsync();
        return (runId, account.Id, identity.Id);
    }

    [Fact]
    public async Task Start_WritesIdentityAndOwnedByCsvs_AndReservesAJobKeyedByTheRun()
    {
        var (runId, accountId, identityId) = await SeedWinnerAsync("gl-writer");
        var s3 = new FakeS3ObjectStore();
        var loader = new FakeBulkLoaderClient();

        var tenantId = Guid.NewGuid();

        var result = await Start(s3, loader).RunAsync(db.NewContext(), new OwnershipRequest(tenantId, RunId: runId));

        Assert.Equal((1, 1), (result.IdentitiesQueuedForLoad, result.EdgesQueuedForLoad));
        Assert.Single(loader.StartedLoads);
        // Thor.Graph's vertex id format (GraphIds, internal to it): {tenant:N}:{type}:{id:N}.
        var identityVertex = $"{tenantId:N}:identity:{identityId:N}";
        var accountVertex = $"{tenantId:N}:account:{accountId:N}";
        Assert.Contains(s3.PutObjects, o => o.Key.EndsWith("vertices/identities.csv") && o.Content.Contains(identityVertex));
        Assert.Contains(s3.PutObjects, o => o.Key.EndsWith("edges/owned_by.csv") && o.Content.Contains("OWNED_BY")
            && o.Content.Contains($"{accountVertex},{identityVertex}"));
        Assert.All(s3.PutObjects, o => Assert.Equal(Bucket, o.Bucket));

        var job = await db.Context.GraphBulkLoadJobs.AsNoTracking().SingleAsync(j => j.ScanId == runId);
        Assert.Equal((runId, "started", result.LoadId), (job.ScanManifestId, job.Status, job.LoadId));
    }

    [Fact]
    public async Task Start_Retried_WhileALoadIsPending_DoesNotStartASecond()
    {
        var (runId, _, _) = await SeedWinnerAsync("gl-retry");
        var loader = new FakeBulkLoaderClient();
        var request = new OwnershipRequest(Guid.NewGuid(), RunId: runId);

        var first = await Start(new FakeS3ObjectStore(), loader).RunAsync(db.NewContext(), request);
        var second = await Start(new FakeS3ObjectStore(), loader).RunAsync(db.NewContext(), request);

        Assert.Single(loader.StartedLoads);
        Assert.Equal(first.LoadId, second.LoadId);
    }

    [Fact]
    public async Task Start_WithNoWinners_StartsNothing_AndPollHasNothingToWaitFor()
    {
        var loader = new FakeBulkLoaderClient();
        var request = new OwnershipRequest(Guid.NewGuid(), RunId: Guid.NewGuid());

        var start = await Start(new FakeS3ObjectStore(), loader).RunAsync(db.NewContext(), request);
        var poll = await Poll(loader).RunAsync(db.NewContext(), request);

        Assert.Null(start.LoadId);
        Assert.Empty(loader.StartedLoads);
        Assert.Equal("no-pending-job", poll.Outcome);
        Assert.False(poll.IsInProgress);
    }

    [Fact]
    public async Task Start_WhenNeptuneRejectsTheLoad_MarksTheJobFailed_SoARetryCanReserveAgain()
    {
        var (runId, _, _) = await SeedWinnerAsync("gl-reject");
        var request = new OwnershipRequest(Guid.NewGuid(), RunId: runId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Start(new FakeS3ObjectStore(), new FakeBulkLoaderClient { FailStart = true }).RunAsync(db.NewContext(), request));
        var retry = await Start(new FakeS3ObjectStore(), new FakeBulkLoaderClient()).RunAsync(db.NewContext(), request);

        Assert.NotNull(retry.LoadId);
        var statuses = await db.Context.GraphBulkLoadJobs.AsNoTracking().Where(j => j.ScanId == runId).Select(j => j.Status).ToListAsync();
        Assert.Equal(["failed", "started"], statuses.OrderBy(s => s));
    }

    [Fact]
    public async Task Poll_ReportsInProgress_ThenCompleted()
    {
        var (runId, _, _) = await SeedWinnerAsync("gl-poll");
        var request = new OwnershipRequest(Guid.NewGuid(), RunId: runId);
        var loader = new FakeBulkLoaderClient(BulkLoadStatus.InProgress, BulkLoadStatus.Completed);
        await Start(new FakeS3ObjectStore(), loader).RunAsync(db.NewContext(), request);

        var first = await Poll(loader).ExecuteAsync(System.Text.Json.JsonSerializer.Serialize(request), CancellationToken.None);
        var second = await Poll(loader).RunAsync(db.NewContext(), request);

        Assert.True(first.IsInProgress);
        Assert.True(Assert.IsType<OwnershipGraphLoadPollResult>(first.Value).IsInProgress);
        Assert.Equal("completed", second.Outcome);
        Assert.Equal("completed", (await db.Context.GraphBulkLoadJobs.AsNoTracking().SingleAsync(j => j.ScanId == runId)).Status);
    }

    [Fact]
    public async Task Poll_WhenTheLoadHasRowErrors_Throws_AndMarksTheJobFailed()
    {
        var (runId, _, _) = await SeedWinnerAsync("gl-rowerrors");
        var request = new OwnershipRequest(Guid.NewGuid(), RunId: runId);
        var loader = new FakeBulkLoaderClient(BulkLoadStatus.Completed) { InsertErrors = 2 };
        await Start(new FakeS3ObjectStore(), loader).RunAsync(db.NewContext(), request);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Poll(loader).RunAsync(db.NewContext(), request));

        Assert.Equal("failed", (await db.Context.GraphBulkLoadJobs.AsNoTracking().SingleAsync(j => j.ScanId == runId)).Status);
    }

    [Fact]
    public async Task Poll_RetriedAfterAFailedLoad_ThrowsAgain_InsteadOfReportingNothingToPoll()
    {
        var (runId, _, _) = await SeedWinnerAsync("gl-retry-failed");
        var request = new OwnershipRequest(Guid.NewGuid(), RunId: runId);
        var loader = new FakeBulkLoaderClient(BulkLoadStatus.Completed) { InsertErrors = 2 };
        await Start(new FakeS3ObjectStore(), loader).RunAsync(db.NewContext(), request);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Poll(loader).RunAsync(db.NewContext(), request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Poll(loader).RunAsync(db.NewContext(), request));
    }

    [Fact]
    public async Task IngestionsPendingJob_ForTheSameManifest_IsNeverMistakenForOwnerships()
    {
        var manifestId = await db.SeedManifestAsync();
        db.Context.GraphBulkLoadJobs.Add(new GraphBulkLoadJob
        {
            Id = Guid.NewGuid(), ScanId = db.ScanId, ScanManifestId = manifestId, LoadId = "ingestion-load",
            S3Uri = "s3://ingestion/", Status = "started", StartedAt = DateTimeOffset.UtcNow,
        });
        await db.Context.SaveChangesAsync();
        var loader = new FakeBulkLoaderClient(BulkLoadStatus.InProgress);

        var poll = await Poll(loader).RunAsync(db.NewContext(), new OwnershipRequest(Guid.NewGuid(), manifestId));

        Assert.Equal("no-pending-job", poll.Outcome);
        Assert.Equal("started", (await db.Context.GraphBulkLoadJobs.AsNoTracking().SingleAsync(j => j.LoadId == "ingestion-load")).Status);
    }
}
