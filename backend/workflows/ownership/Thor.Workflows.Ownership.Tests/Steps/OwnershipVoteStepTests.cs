using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Thor.Workflows.Ownership.Constants;
using Thor.Workflows.Ownership.Models;
using Thor.Workflows.Ownership.Steps;
using Thor.Workflows.Ownership.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Steps;

/// <summary>
/// The Map body end to end against a real Postgres, focused on what chunking changed: windows that
/// stay put while the run writes under them, and chunks of one phase that cannot see each other's
/// results. Also covers group and asset votes, which the account-only branch tests never flushed.
/// </summary>
public sealed class OwnershipVoteStepTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => db.SeedRulesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private OwnershipVoteStep Step() => new(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.NewContext()));

    private async Task<OwnershipChunkSummary> VoteAsync(Guid runId, string entityType, int offset, int limit, string scope = OwnershipAssignmentScope.UnassignedOnly)
    {
        var request = new OwnershipChunkRequest(Guid.NewGuid(), null, runId, scope, entityType, offset, limit);
        return await Step().RunAsync(db.NewContext(), db.NewContext(), request, CancellationToken.None);
    }

    /// <summary>
    /// Fails without the scope ignoring this run's own rows: chunk 0 assigns its accounts, which drops
    /// them out of "unassigned", and chunk 1's offset then lands past the accounts it was meant to
    /// cover.
    /// </summary>
    [Fact]
    public async Task Windows_StayPut_WhileTheRunAssignsTheEntitiesInThem()
    {
        var accounts = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var identity = await db.AddIdentityAsync($"S{i}", $"Stable {i}", $"stable{i}@corp.com");
            accounts.Add((await db.AddAccountAsync($"stable-{i}", identity.Email)).Id);
        }

        // Only these five are unassigned: every other account a class-mate seeded gets a prior owner.
        var others = await db.Context.Accounts.Where(a => !accounts.Contains(a.Id)).Select(a => a.Id).ToListAsync();
        var anyIdentity = await db.Context.Identities.Select(i => i.Id).FirstAsync();
        foreach (var other in others)
        {
            await db.AddPriorOwnerAsync("account", other, anyIdentity);
        }

        var runId = Guid.NewGuid();
        var scanned = new List<int>();
        for (var offset = 0; offset < 6; offset += 2)
        {
            scanned.Add((await VoteAsync(runId, "account", offset, 2)).EntitiesScanned);
        }

        Assert.Equal([2, 2, 1], scanned);
        var assigned = await db.Context.PartyAssignments
            .Where(pa => pa.RunId == runId.ToString() && pa.Rank == 1)
            .Select(pa => pa.EntityId)
            .ToListAsync();
        Assert.Equal(accounts.OrderBy(a => a), assigned.OrderBy(a => a));
    }

    /// <summary>
    /// Same display name, so the sibling rule would hand the second account whatever owner the first
    /// just got — if a chunk could see an earlier chunk's results from the same phase. It must not:
    /// the branch's single phase query never could, and the result would otherwise depend on which
    /// window ran first.
    /// </summary>
    [Fact]
    public async Task ChunksOfOnePhase_DoNotFeedEachOther()
    {
        var identity = await db.AddIdentityAsync("SIB1", "Sibling Owner", "sibling-owner@corp.com");
        var withEmail = await db.AddAccountAsync("twin-a", identity.Email);
        var withoutEmail = await db.AddAccountAsync("twin-b");
        foreach (var account in new[] { withEmail, withoutEmail })
        {
            account.DisplayName = "IdenticalTwinName";
        }
        await db.Context.SaveChangesAsync();

        // Scope "all" keeps every account in the window, so each twin's position in id order is its
        // offset. Twin A's window runs first and assigns it; twin B's window runs after.
        var ordered = await db.Context.Accounts.Where(a => !a.IsDeleted).OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        var runId = Guid.NewGuid();
        var first = await VoteAsync(runId, "account", ordered.IndexOf(withEmail.Id), 1, OwnershipAssignmentScope.All);
        await VoteAsync(runId, "account", ordered.IndexOf(withoutEmail.Id), 1, OwnershipAssignmentScope.All);

        Assert.Equal(1, first.NewRankOneAssignments);
        var twinB = await db.Context.PartyAssignments
            .Where(pa => pa.RunId == runId.ToString() && pa.EntityId == withoutEmail.Id)
            .ToListAsync();
        Assert.Empty(twinB);
    }

    [Fact]
    public async Task GroupVotes_Flush_WithTheGroupsOwnId()
    {
        var identity = await db.AddIdentityAsync("GV1", "Group Manager", "group-manager@corp.com");
        var manager = await db.AddAccountAsync("gv-manager", identity.Email);
        await db.AddPriorOwnerAsync("account", manager.Id, identity.Id);
        var group = await db.AddGroupAsync("gv-team");
        await db.AddEdgeAsync(group.Id, "grp", manager.Id, "account", "MANAGED_BY");

        var runId = Guid.NewGuid();
        await VoteAsync(runId, "grp", 0, int.MaxValue);

        var vote = await db.Context.OwnershipVotes.SingleAsync(v => v.RunId == runId.ToString() && v.EntityId == group.Id);
        Assert.Equal(identity.Id, vote.VotedFor);
    }

    [Fact]
    public async Task AssetVotes_ReadTheWalkStaging_ThenFlush()
    {
        var identity = await db.AddIdentityAsync("AV1", "Share Owner", "asset-share-owner@corp.com");
        var root = await db.AddAssetAsync("av-root");
        var leaf = await db.AddAssetAsync("av-leaf", root.Id);
        await db.AddPriorOwnerAsync("asset", root.Id, identity.Id);

        var runId = Guid.NewGuid();
        var walk = await new OwnershipWalkStep(NullLoggerFactory.Instance, new FakeTenantConnectionManager(_ => db.NewContext()), TimeSpan.MaxValue)
            .RunAsync(db.NewContext(), new OwnershipWalkRequest(new OwnershipRequest(Guid.NewGuid(), RunId: runId), "asset"));
        Assert.False(walk.IsInProgress);

        await VoteAsync(runId, "asset", 0, int.MaxValue);

        var assignment = await db.Context.PartyAssignments.SingleAsync(pa => pa.RunId == runId.ToString() && pa.EntityId == leaf.Id);
        Assert.Equal(identity.Id, assignment.IdentityId);
        Assert.True(await db.Context.OwnershipVotes.AnyAsync(v => v.RunId == runId.ToString() && v.EntityId == leaf.Id));
    }
}
