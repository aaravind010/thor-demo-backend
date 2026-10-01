using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Ownership.Matching;
using Thor.Workflows.Ownership.Persistence;
using Thor.Rules.Ownership;
using Thor.Workflows.Ownership.Tests.Fixtures;
using Xunit;

namespace Thor.Workflows.Ownership.Tests.Persistence;

/// <summary>
/// Exercises <see cref="OwnershipResultWriter"/>'s idempotency guarantees against a real Postgres
/// (Testcontainers) — mirrors <c>AtreResultWriterTests</c>, extended for Ownership's
/// history-keeping (multi-rank) shape.
/// </summary>
public sealed class OwnershipResultWriterTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly Guid _ruleId = Guid.NewGuid();
    private readonly Guid _secondRuleId = Guid.NewGuid();

    private TenantDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _context = NewContext();
        await _context.Database.EnsureCreatedAsync();

        _context.AccountTypes.Add(new AccountType
        {
            Id = WellKnownAccountTypes.Unclassified, Name = "Unclassified", Description = "d", IsHuman = false,
        });

        // ownership_vote.rule_id is a real FK to ownership_rule — seed the rules the tests refer to.
        var now = DateTimeOffset.UtcNow;
        _context.OwnershipRules.AddRange(
            new OwnershipRule
            {
                Id = _ruleId, RuleName = "test-rule", RuleType = OwnershipRuleType.FieldMatch, RuleDefinition = "{}",
                AppliesTo = "account", IsActive = true, PrecisionScore = 0.90m, CreatedAt = now, UpdatedAt = now,
            },
            new OwnershipRule
            {
                Id = _secondRuleId, RuleName = "test-rule-2", RuleType = OwnershipRuleType.FieldMatch, RuleDefinition = "{}",
                AppliesTo = "account", IsActive = true, PrecisionScore = 0.40m, CreatedAt = now, UpdatedAt = now,
            });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private TenantDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

    private async Task<(Guid AccountId, Guid IdentityId)> SeedAccountAndIdentityAsync()
    {
        var sourceId = Guid.NewGuid();
        _context.Sources.Add(new Source
        {
            Id = sourceId, ConnectorType = 308, Name = "s", Config = "{}", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        var account = OwnershipSeedData.BuildAccount(sourceId, "n1", email: "n1@test.com");
        _context.Accounts.Add(account);

        var identity = OwnershipSeedData.BuildIdentity("E1", "N1 Test", "n1@test.com");
        _context.Identities.Add(identity);

        await _context.SaveChangesAsync();
        return (account.Id, identity.Id);
    }

    [Fact]
    public async Task FlushAsync_TwoFlushesSameAccountSameRunId_SecondIsFullyIdempotent()
    {
        var (accountId, identityId) = await SeedAccountAndIdentityAsync();
        var runId = Guid.NewGuid();
        var candidates = new List<OwnershipCandidateRow> { new(accountId, identityId, _ruleId, 0.90m) };

        var writer1 = new OwnershipResultWriter(_context, runId, NullLogger.Instance);
        writer1.Stage("account", accountId, candidates, DateTimeOffset.UtcNow);
        var firstFlush = await writer1.FlushAsync();

        var writer2 = new OwnershipResultWriter(_context, runId, NullLogger.Instance);
        writer2.Stage("account", accountId, candidates, DateTimeOffset.UtcNow);
        var secondFlush = await writer2.FlushAsync();

        Assert.Equal(1, firstFlush.NewAssignments);
        Assert.Equal(1, firstFlush.NewRankOneAssignments);
        Assert.Equal(0, secondFlush.NewAssignments);

        Assert.Equal(1, await _context.PartyAssignments.CountAsync(a => a.EntityId == accountId));
        Assert.Equal(1, await _context.OwnershipVotes.CountAsync(v => v.EntityId == accountId));
        Assert.Equal(1, await _context.PartyAssignmentEvents.CountAsync());
    }

    [Fact]
    public async Task FlushAsync_MultipleCandidatesSameAccount_WritesOneRowPerRank_OneVotePerRule()
    {
        var (accountId, identityId1) = await SeedAccountAndIdentityAsync();
        var identity2 = OwnershipSeedData.BuildIdentity("E2", "N2 Test", "n2@test.com");
        _context.Identities.Add(identity2);
        await _context.SaveChangesAsync();

        var runId = Guid.NewGuid();
        var candidates = new List<OwnershipCandidateRow>
        {
            new(accountId, identityId1, _ruleId, 0.90m),
            new(accountId, identity2.Id, _secondRuleId, 0.40m),
        };

        var writer = new OwnershipResultWriter(_context, runId, NullLogger.Instance);
        writer.Stage("account", accountId, candidates, DateTimeOffset.UtcNow);
        var flush = await writer.FlushAsync();

        Assert.Equal(2, flush.NewAssignments);
        Assert.Equal(1, flush.NewRankOneAssignments);
        Assert.Equal(2, await _context.OwnershipVotes.CountAsync(v => v.EntityId == accountId));
        Assert.Equal(2, await _context.PartyAssignments.CountAsync(a => a.EntityId == accountId));
        Assert.Equal(2, await _context.PartyAssignmentEvents.CountAsync());

        var rankTwo = await _context.PartyAssignments.SingleAsync(a => a.EntityId == accountId && a.Rank == 2);
        Assert.Contains(_secondRuleId.ToString(), rankTwo.PrecisionScoreSnapshot);
    }

    [Fact]
    public async Task FlushAsync_RetryWithSameRunId_ContinuesWithoutDuplicating()
    {
        // The specific correctness property that lets the POC drop its ownership_intent park
        // table: a "retried" writer for the same run id re-staging an already-committed
        // candidate adds nothing new.
        var (accountId, identityId) = await SeedAccountAndIdentityAsync();
        var runId = Guid.NewGuid();
        var candidates = new List<OwnershipCandidateRow> { new(accountId, identityId, _ruleId, 0.90m) };

        var writer1 = new OwnershipResultWriter(_context, runId, NullLogger.Instance);
        writer1.Stage("account", accountId, candidates, DateTimeOffset.UtcNow);
        await writer1.FlushAsync();

        var writer2 = new OwnershipResultWriter(_context, runId, NullLogger.Instance);
        writer2.Stage("account", accountId, candidates, DateTimeOffset.UtcNow);
        var retryFlush = await writer2.FlushAsync();

        Assert.Equal(0, retryFlush.NewAssignments);
        Assert.Equal(1, await _context.PartyAssignments.CountAsync(a => a.EntityId == accountId));
    }

    [Fact]
    public async Task FlushAsync_DifferentRunIdSameAccount_VoteHistoryNotDeduplicated()
    {
        var (accountId, identityId) = await SeedAccountAndIdentityAsync();
        var candidates = new List<OwnershipCandidateRow> { new(accountId, identityId, _ruleId, 0.90m) };

        var writer1 = new OwnershipResultWriter(_context, Guid.NewGuid(), NullLogger.Instance);
        writer1.Stage("account", accountId, candidates, DateTimeOffset.UtcNow);
        await writer1.FlushAsync();

        var writer2 = new OwnershipResultWriter(_context, Guid.NewGuid(), NullLogger.Instance);
        writer2.Stage("account", accountId, candidates, DateTimeOffset.UtcNow);
        await writer2.FlushAsync();

        // Both ownership_vote's (entity_id, rule_id, run_id) key and party_assignment's
        // (entity_type, entity_id, identity_id, run_id) key include run_id, so a genuinely
        // different run id is a different idempotency key for both tables — nothing here gets
        // deduplicated across distinct runs. (In the real pipeline this exact input never
        // recurs: after the first run the account has an active rank-1 assignment, so the
        // candidate query's own scoping predicate excludes it from any later run entirely — see
        // OwnershipCandidateSqlTests. This test only exercises the writer's own constraint in
        // isolation, bypassing that scoping.)
        Assert.Equal(2, await _context.OwnershipVotes.CountAsync(v => v.EntityId == accountId));
        Assert.Equal(2, await _context.PartyAssignments.CountAsync(a => a.EntityId == accountId));
    }
}
