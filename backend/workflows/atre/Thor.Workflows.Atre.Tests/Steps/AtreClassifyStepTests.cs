using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Atre.Models;
using Thor.Workflows.Atre.Steps;
using Thor.Workflows.Atre.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Steps;

/// <summary>
/// End-to-end exercise of <see cref="AtreClassifyStep"/> against a real Postgres, using the 8
/// confirmed ATRE rules. Covers classification, idempotency, and the windowing that lets a
/// Distributed Map run several chunks of one run at once.
///
/// <para>Workflow-row bookkeeping is deliberately not asserted here — it belongs to
/// <see cref="AtreStartRunStep"/> and <see cref="AtreFinalizeStep"/>, whose own suites cover it.</para>
/// </summary>
public sealed class AtreClassifyStepTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private const int WholeScope = int.MaxValue;

    /// <summary>Mirrors production: a fresh read connection and a fresh write connection per invocation.</summary>
    private async Task<AtreChunkSummary> RunAsync(AtreChunkRequest request)
    {
        var step = new AtreClassifyStep(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.Context));
        await using var readContext = db.NewContext();
        await using var writeContext = db.NewContext();
        return await step.RunAsync(readContext, writeContext, request, CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_EightRuleFixture_ClassifiesEachAccountAndIsIdempotentOnRerun()
    {
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync(
            ("human-1", "user"), ("svc-1", "service_principal"), ("comp-1", "computer"), ("unmatched-1", "unknown_kind"));
        await db.MarkChangedInManifestAsync(manifestId, accounts);
        var (human, serviceAccount, computer, unmatched) = (accounts[0], accounts[1], accounts[2], accounts[3]);

        var request = new AtreChunkRequest(Guid.NewGuid(), manifestId, RunId: null, Offset: 0, Limit: WholeScope);
        var summary = await RunAsync(request);

        Assert.Equal(4, summary.AccountsScanned);
        Assert.Equal(3, summary.AccountsAssigned);
        Assert.Equal(1, summary.AccountsWithNoFiringRule);
        Assert.Equal(3, summary.NewAssignments);

        Assert.Equal(AtreSeedData.HumanId, await AccountTypeOfAsync(human.Id));
        Assert.Equal(AtreSeedData.ServiceAccountId, await AccountTypeOfAsync(serviceAccount.Id));
        Assert.Equal(AtreSeedData.ComputerId, await AccountTypeOfAsync(computer.Id));
        Assert.Equal(WellKnownAccountTypes.Unclassified, await AccountTypeOfAsync(unmatched.Id));

        // Zero-firing-rule accounts get no assignment row anywhere — matches the POC exactly.
        Assert.False(await db.Context.AccountTypeAssignments.AnyAsync(a => a.EntityId == unmatched.Id));

        var rerun = await RunAsync(request);

        Assert.Equal(0, rerun.NewAssignments);
        var ids = accounts.Select(a => a.Id).ToList();
        Assert.Equal(3, await db.Context.AccountTypeAssignments.CountAsync(a => ids.Contains(a.EntityId)));
    }

    /// <summary>
    /// The property the whole fan-out rests on: consecutive windows are disjoint and together cover
    /// the scope exactly. Overlap would only waste work; a gap would silently leave accounts
    /// unclassified.
    /// </summary>
    [Fact]
    public async Task RunAsync_ConsecutiveChunks_ClassifyEachAccountExactlyOnce()
    {
        const int ChunkSize = 10;
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync([.. Enumerable.Range(0, 40).Select(i => ($"chunked-{i}", "user"))]);
        await db.MarkChangedInManifestAsync(manifestId, accounts);
        var tenantId = Guid.NewGuid();

        var scanned = 0;
        var assigned = 0;
        for (var offset = 0; offset < 40; offset += ChunkSize)
        {
            var summary = await RunAsync(new AtreChunkRequest(tenantId, manifestId, null, offset, ChunkSize));
            Assert.Equal(ChunkSize, summary.AccountsScanned);
            scanned += summary.AccountsScanned;
            assigned += summary.NewAssignments;
        }

        Assert.Equal(40, scanned);
        Assert.Equal(40, assigned);

        var ids = accounts.Select(a => a.Id).ToList();
        Assert.Equal(40, await db.Context.AccountTypeAssignments.CountAsync(a => ids.Contains(a.EntityId)));
        Assert.False(await db.Context.Accounts.AnyAsync(a => ids.Contains(a.Id) && a.AccountTypeId == WellKnownAccountTypes.Unclassified));
    }

    /// <summary>
    /// How a run learns it has finished: the chunk that runs off the end reads nothing, and
    /// <see cref="AtreWavePlanner"/> reads that zero rather than anyone having counted first.
    /// </summary>
    [Fact]
    public async Task RunAsync_ChunkPastTheEndOfTheScope_ScansNothingAndSucceeds()
    {
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync(("only-1", "user"), ("only-2", "user"));
        await db.MarkChangedInManifestAsync(manifestId, accounts);

        var summary = await RunAsync(new AtreChunkRequest(Guid.NewGuid(), manifestId, null, Offset: 100, Limit: 10));

        Assert.Equal(0, summary.AccountsScanned);
        Assert.Equal(0, summary.NewAssignments);
    }

    /// <summary>A short final chunk is normal — it is what tells the run there is no more.</summary>
    [Fact]
    public async Task RunAsync_ChunkStraddlingTheEnd_ReadsOnlyWhatIsThere()
    {
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync([.. Enumerable.Range(0, 7).Select(i => ($"straddle-{i}", "user"))]);
        await db.MarkChangedInManifestAsync(manifestId, accounts);

        var summary = await RunAsync(new AtreChunkRequest(Guid.NewGuid(), manifestId, null, Offset: 5, Limit: 10));

        Assert.Equal(2, summary.AccountsScanned);
    }

    private async Task<Guid> AccountTypeOfAsync(Guid accountId) =>
        (await db.Context.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId)).AccountTypeId;
}
