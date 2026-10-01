using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Atre.Streaming;
using Thor.Workflows.Atre.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Streaming;

/// <summary>
/// Exercises <see cref="AccountStreamReader"/> against a real Postgres. The windowing is the thing
/// worth testing here: the chunks of a run are disjoint and complete only because the underlying
/// ordering is total and stable.
/// </summary>
public sealed class AccountStreamReaderTests(TenantDatabaseFixture db) : IClassFixture<TenantDatabaseFixture>
{
    private const int WholeScope = int.MaxValue;

    private async Task<List<Account>> StreamAsync(Guid? scanManifestId, int offset = 0, int limit = WholeScope)
    {
        var streamed = new List<Account>();
        await foreach (var account in new AccountStreamReader(db.Context).StreamAsync(scanManifestId, offset, limit))
        {
            streamed.Add(account);
        }
        return streamed;
    }

    [Fact]
    public async Task StreamAsync_ManifestScoped_ReturnsOnlyAccountsInThatManifestsChangeEvents()
    {
        var manifestId = await db.SeedManifestAsync();
        var otherManifestId = await db.SeedManifestAsync();
        var inScope = await db.SeedAccountsAsync(("in-scope", "user"));
        var outOfScope = await db.SeedAccountsAsync(("out-of-scope", "user"));
        await db.MarkChangedInManifestAsync(manifestId, inScope);
        await db.MarkChangedInManifestAsync(otherManifestId, outOfScope);

        var streamed = await StreamAsync(manifestId);

        Assert.Single(streamed);
        Assert.Equal(inScope[0].Id, streamed[0].Id);
    }

    [Fact]
    public async Task StreamAsync_NoManifest_ReturnsEveryAccount()
    {
        await db.SeedAccountsAsync(("all-1", "user"), ("all-2", "user"), ("all-3", "user"));

        var streamed = await StreamAsync(null);

        Assert.Equal(await db.Context.Accounts.CountAsync(), streamed.Count);
    }

    /// <summary>
    /// The guarantee every chunk depends on. Overlap would only waste work; a gap would silently
    /// leave accounts unclassified, so this asserts an exact partition of the scope.
    /// </summary>
    [Fact]
    public async Task StreamAsync_ConsecutiveWindows_CoverTheScopeExactlyOnce()
    {
        const int Window = 10;
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync([.. Enumerable.Range(0, 50).Select(i => ($"window-{i}", "user"))]);
        await db.MarkChangedInManifestAsync(manifestId, accounts);

        var seen = new List<Guid>();
        for (var offset = 0; offset < 50; offset += Window)
        {
            seen.AddRange((await StreamAsync(manifestId, offset, Window)).Select(a => a.Id));
        }

        Assert.Equal(50, seen.Count);
        Assert.Equal(50, seen.Distinct().Count());
        Assert.Equal([.. accounts.Select(a => a.Id).Order()], [.. seen.Order()]);
    }

    /// <summary>
    /// Windows are taken over <c>ORDER BY id</c> rather than over anything ATRE writes — a run
    /// updates <c>account.account_type_id</c> as it goes, so ordering on that would reshuffle the
    /// rows underneath the very chunks reading them.
    /// </summary>
    [Fact]
    public async Task StreamAsync_WindowsFollowIdOrder_SoTheyDoNotShiftAsTheRunWrites()
    {
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync([.. Enumerable.Range(0, 12).Select(i => ($"ordered-{i}", "user"))]);
        await db.MarkChangedInManifestAsync(manifestId, accounts);

        var expected = accounts.Select(a => a.Id).Order().ToList();

        Assert.Equal(expected.Take(4), (await StreamAsync(manifestId, 0, 4)).Select(a => a.Id));
        Assert.Equal(expected.Skip(4).Take(4), (await StreamAsync(manifestId, 4, 4)).Select(a => a.Id));
        Assert.Equal(expected.Skip(8).Take(4), (await StreamAsync(manifestId, 8, 4)).Select(a => a.Id));
    }

    [Fact]
    public async Task StreamAsync_WindowStraddlingTheEnd_ReturnsOnlyWhatIsThere()
    {
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync([.. Enumerable.Range(0, 7).Select(i => ($"straddling-{i}", "user"))]);
        await db.MarkChangedInManifestAsync(manifestId, accounts);

        Assert.Equal(2, (await StreamAsync(manifestId, offset: 5, limit: 10)).Count);
    }

    /// <summary>How a run discovers it has finished — no count, just a window that reads nothing.</summary>
    [Fact]
    public async Task StreamAsync_WindowPastTheEnd_ReturnsNothing()
    {
        var manifestId = await db.SeedManifestAsync();
        var accounts = await db.SeedAccountsAsync(("past-end-1", "user"));
        await db.MarkChangedInManifestAsync(manifestId, accounts);

        Assert.Empty(await StreamAsync(manifestId, offset: 500, limit: 10));
    }
}
