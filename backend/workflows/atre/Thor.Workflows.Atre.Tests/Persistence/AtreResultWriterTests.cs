using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.Workflows.Atre.Persistence;
using Thor.Workflows.Atre.Voting;
using Xunit;

namespace Thor.Workflows.Atre.Tests.Persistence;

/// <summary>
/// Exercises <see cref="AtreResultWriter"/>'s idempotency guarantees against a real Postgres
/// (Testcontainers) — this is the correctness fix for the POC's race-prone select-then-insert
/// dedup, so it's verified directly rather than only through the full <c>AtreClassifyStep</c>.
/// </summary>
public sealed class AtreResultWriterTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly Guid _humanTypeId = Guid.NewGuid();
    private readonly Guid _ruleId = Guid.NewGuid();

    private TenantDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _context = NewContext();
        await _context.Database.EnsureCreatedAsync();

        _context.AccountTypes.Add(new AccountType { Id = WellKnownAccountTypes.Unclassified, Name = "Unclassified", Description = "d", IsHuman = false });
        _context.AccountTypes.Add(new AccountType { Id = _humanTypeId, Name = "Human", Description = "d", IsHuman = true });
        await _context.SaveChangesAsync();

        // atre_vote.rule_id is a real FK to account_type_rule — seed the rule _ruleId refers to.
        _context.AccountTypeRules.Add(new AccountTypeRule
        {
            Id = _ruleId,
            RuleName = "test-rule",
            RuleDefinition = """{"field":"account_kind","operator":"equals","value":"user"}""",
            AppliesTo = "account",
            TargetAccountTypeId = _humanTypeId,
            IsActive = true,
            PrecisionScore = 0.55m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
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

    private async Task<Account> SeedAccountAsync()
    {
        var sourceId = Guid.NewGuid();
        _context.Sources.Add(new Source
        {
            Id = sourceId, ConnectorType = 308, Name = "s", Config = "{}", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        var account = new Account
        {
            Id = Guid.NewGuid(), SourceId = sourceId, ConnectorType = 308, NativeId = "n1",
            AccountKind = "user", IsHuman = true, DisplayName = "n1", SamAccountName = "n1",
            Upn = "n1@test.com", Email = "n1@test.com", DomainName = "test.com", FilerName = "",
            NativeAccountId = "n1", IsDeleted = false, IsDisabled = false,
            AccountTypeId = WellKnownAccountTypes.Unclassified, RawAttributes = "{}",
            ContentHash = "h1", HashVersion = 1, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    private AccountDecision Decision() => new(
        WinningAccountTypeId: _humanTypeId,
        VoteDistribution: new Dictionary<Guid, decimal> { [_humanTypeId] = 0.55m },
        ContributingRuleIds: [_ruleId],
        PrecisionScoreSnapshot: new Dictionary<Guid, decimal> { [_ruleId] = 0.55m });

    [Fact]
    public async Task FlushAsync_TwoFlushesSameAccountSameRunId_SecondIsFullyIdempotent()
    {
        var account = await SeedAccountAsync();
        var runId = Guid.NewGuid();
        var votes = new List<AccountVote> { new(_ruleId, _humanTypeId, 0.55m) };

        var writer1 = new AtreResultWriter(_context, runId, NullLogger.Instance);
        writer1.Stage(account, Decision(), votes, DateTimeOffset.UtcNow);
        var firstFlush = await writer1.FlushAsync();

        var writer2 = new AtreResultWriter(_context, runId, NullLogger.Instance);
        writer2.Stage(account, Decision(), votes, DateTimeOffset.UtcNow);
        var secondFlush = await writer2.FlushAsync();

        Assert.Equal(1, firstFlush.NewAssignments);
        Assert.Equal(0, secondFlush.NewAssignments);

        Assert.Equal(1, await _context.AccountTypeAssignments.CountAsync(a => a.EntityId == account.Id));
        Assert.Equal(1, await _context.AtreVotes.CountAsync(v => v.EntityId == account.Id));
        Assert.Equal(1, await _context.AccountTypeAssignmentEvents.CountAsync());

        var refreshed = await _context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id);
        Assert.Equal(_humanTypeId, refreshed.AccountTypeId);
    }

    [Fact]
    public async Task FlushAsync_DifferentRunIdSameAccount_AssignmentStaysIdempotentButVotesDoNot()
    {
        var account = await SeedAccountAsync();
        var votes = new List<AccountVote> { new(_ruleId, _humanTypeId, 0.55m) };

        var writer1 = new AtreResultWriter(_context, Guid.NewGuid(), NullLogger.Instance);
        writer1.Stage(account, Decision(), votes, DateTimeOffset.UtcNow);
        var firstFlush = await writer1.FlushAsync();

        var writer2 = new AtreResultWriter(_context, Guid.NewGuid(), NullLogger.Instance);
        writer2.Stage(account, Decision(), votes, DateTimeOffset.UtcNow);
        var secondFlush = await writer2.FlushAsync();

        Assert.Equal(1, firstFlush.NewAssignments);
        // A new run id does NOT let a second flush re-claim an account another run already
        // assigned — assignment is a permanent, one-time claim per (entity_id, entity_type).
        Assert.Equal(0, secondFlush.NewAssignments);

        Assert.Equal(1, await _context.AccountTypeAssignments.CountAsync(a => a.EntityId == account.Id));
        // Votes are keyed by (entity_id, rule_id, run_id) — a different run id is a different
        // idempotency key by design, so vote history is NOT deduplicated across distinct runs.
        Assert.Equal(2, await _context.AtreVotes.CountAsync(v => v.EntityId == account.Id));
    }
}
