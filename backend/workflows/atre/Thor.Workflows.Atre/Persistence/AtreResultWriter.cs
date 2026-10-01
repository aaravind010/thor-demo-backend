using System.Text.Json;
using Microsoft.Extensions.Logging;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Atre.Constants;
using Thor.Workflows.Atre.Voting;

namespace Thor.Workflows.Atre.Persistence;

internal sealed record FlushResult(int VotesWritten, int NewAssignments);

/// <summary>
/// Stages ATRE decisions in memory and flushes them as one batch: votes are always attempted
/// (idempotent via <see cref="AtreVote"/>'s DB unique constraint), assignments upsert atomically
/// (idempotent via <see cref="AccountTypeAssignment"/>'s DB unique constraint — only accounts not
/// already assigned actually insert), and — for ONLY the accounts the upsert actually inserted
/// this call — an assignment-event audit row and a batched <c>account.account_type_id</c> stamp.
/// This is what makes two racing/retried flushes over overlapping accounts safe: each writer only
/// emits side effects for rows it itself won the insert race for, replacing the POC's race-prone
/// read-then-write "already assigned?" check with an atomic DB-level guard.
/// </summary>
internal sealed class AtreResultWriter(TenantDbContext writeContext, Guid runId, ILogger logger)
{
    private readonly string _runIdText = runId.ToString();
    private readonly IAtreVoteRepository _voteRepository = new AtreVoteRepository(writeContext);
    private readonly IAccountTypeAssignmentRepository _assignmentRepository = new AccountTypeAssignmentRepository(writeContext);
    private readonly IAccountRepository _accountRepository = new AccountRepository(writeContext);

    private readonly List<AtreVote> _pendingVotes = [];
    private readonly List<(AccountTypeAssignment Assignment, Guid PreviousTypeId)> _pendingAssignments = [];

    public int PendingCount => _pendingAssignments.Count;

    public void Stage(Account account, AccountDecision decision, IReadOnlyList<AccountVote> votes, DateTimeOffset now)
    {
        foreach (var vote in votes)
        {
            _pendingVotes.Add(new AtreVote
            {
                Id = Guid.NewGuid(),
                EntityId = account.Id,
                RuleId = vote.RuleId,
                VotedFor = vote.TargetAccountTypeId,
                VoteWeight = vote.Weight,
                RunId = _runIdText,
                CreatedAt = now,
            });
        }

        _pendingAssignments.Add((
            new AccountTypeAssignment
            {
                Id = Guid.NewGuid(),
                EntityType = ScopeConstants.AccountEntityType,
                EntityId = account.Id,
                AccountTypeId = decision.WinningAccountTypeId,
                Method = ScopeConstants.RuleMethod,
                IsOverride = false,
                VoteDistribution = JsonSerializer.Serialize(decision.VoteDistribution),
                ContributingRuleIds = [.. decision.ContributingRuleIds],
                PrecisionScoreSnapshot = JsonSerializer.Serialize(decision.PrecisionScoreSnapshot),
                RunId = _runIdText,
                AssignedAt = now,
            },
            account.AccountTypeId));
    }

    public async Task<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingVotes.Count == 0 && _pendingAssignments.Count == 0)
        {
            return new FlushResult(0, 0);
        }

        await _voteRepository.UpsertBatchAsync(_pendingVotes, cancellationToken);
        var votesWritten = _pendingVotes.Count;
        _pendingVotes.Clear();

        var assignments = new List<(AccountTypeAssignment Assignment, Guid PreviousTypeId)>(_pendingAssignments);
        _pendingAssignments.Clear();

        if (assignments.Count == 0)
        {
            return new FlushResult(votesWritten, 0);
        }

        var insertedEntityIds = new HashSet<Guid>(
            await _assignmentRepository.UpsertBatchAsync([.. assignments.Select(a => a.Assignment)], cancellationToken));

        var now = DateTimeOffset.UtcNow;
        var accountTypeStamps = new Dictionary<Guid, Guid>();

        foreach (var (assignment, previousTypeId) in assignments)
        {
            if (!insertedEntityIds.Contains(assignment.EntityId))
            {
                continue; // another run already claimed this account — no side effects for us
            }

            writeContext.AccountTypeAssignmentEvents.Add(new AccountTypeAssignmentEvent
            {
                Id = Guid.NewGuid(),
                AssignmentId = assignment.Id,
                EventType = ScopeConstants.AssignedEventType,
                PreviousTypeId = previousTypeId,
                NewTypeId = assignment.AccountTypeId,
                Actor = ScopeConstants.AtreWorkflowActor,
                RunId = runId,
                OccurredAt = now,
            });

            accountTypeStamps[assignment.EntityId] = assignment.AccountTypeId;
        }

        await writeContext.SaveChangesAsync(cancellationToken);
        await _accountRepository.BulkUpdateAccountTypeAsync(accountTypeStamps, cancellationToken);

        logger.LogInformation(
            "ATRE flush for run {RunId}: {VotesWritten} votes staged, {NewAssignments}/{TotalStaged} assignments newly inserted.",
            runId, votesWritten, insertedEntityIds.Count, assignments.Count);

        return new FlushResult(votesWritten, insertedEntityIds.Count);
    }
}
