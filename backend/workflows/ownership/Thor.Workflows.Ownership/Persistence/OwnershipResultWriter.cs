using System.Text.Json;
using Microsoft.Extensions.Logging;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;
using Thor.DataLayer.Repositories;
using Thor.Workflows.Ownership.Constants;
using Thor.Workflows.Ownership.Matching;

namespace Thor.Workflows.Ownership.Persistence;

internal sealed record FlushResult(int VotesWritten, int NewAssignments, int NewRankOneAssignments);

/// <summary>
/// Stages one entity's ranked candidates in memory and flushes them as one batch: votes are
/// always attempted (idempotent via <see cref="OwnershipVote"/>'s DB unique constraint),
/// assignments upsert atomically (idempotent via <see cref="PartyAssignment"/>'s DB unique
/// constraint — only rows not already inserted this run actually insert), and — for ONLY the rows
/// the upsert actually inserted this call — a <see cref="PartyAssignmentEvent"/> audit row. This is
/// what makes two racing/retried flushes over overlapping accounts safe: each writer only emits
/// side effects for rows it itself won the insert race for.
/// </summary>
internal sealed class OwnershipResultWriter(TenantDbContext writeContext, Guid runId, ILogger logger)
{
    private readonly string _runIdText = runId.ToString();
    private readonly IOwnershipVoteRepository _voteRepository = new OwnershipVoteRepository(writeContext);
    private readonly IPartyAssignmentRepository _assignmentRepository = new PartyAssignmentRepository(writeContext);

    private readonly List<OwnershipVote> _pendingVotes = [];
    private readonly List<PartyAssignment> _pendingAssignments = [];

    public int PendingCount => _pendingAssignments.Count;

    /// <summary>Stages every ranked candidate for one entity (already weight-desc ordered by the query).</summary>
    public void Stage(string entityType, Guid entityId, IReadOnlyList<OwnershipCandidateRow> candidates, DateTimeOffset now)
    {
        var voteDistribution = candidates.ToDictionary(c => c.IdentityId.ToString(), c => c.Weight);
        var contributingRuleIds = candidates.Select(c => c.RuleId).Distinct().ToArray();

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var rank = i + 1;
            var assignmentId = Guid.NewGuid();

            _pendingVotes.Add(new OwnershipVote
            {
                Id = Guid.NewGuid(),
                EntityId = entityId,
                RuleId = candidate.RuleId,
                VotedFor = candidate.IdentityId,
                VoteWeight = candidate.Weight,
                RunId = _runIdText,
                CreatedAt = now,
            });

            var precisionSnapshot = JsonSerializer.Serialize(new Dictionary<string, decimal> { [candidate.RuleId.ToString()] = candidate.Weight });

            _pendingAssignments.Add(new PartyAssignment
            {
                Id = assignmentId,
                EntityType = entityType,
                EntityId = entityId,
                IdentityId = candidate.IdentityId,
                Rank = rank,
                IsActive = true,
                IsOverride = false,
                VoteDistribution = JsonSerializer.Serialize(voteDistribution),
                ContributingRuleIds = contributingRuleIds,
                PrecisionScoreSnapshot = precisionSnapshot,
                RunId = _runIdText,
                AssignedAt = now,
            });
        }
    }

    public async Task<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingVotes.Count == 0 && _pendingAssignments.Count == 0)
        {
            return new FlushResult(0, 0, 0);
        }

        await _voteRepository.UpsertBatchAsync(_pendingVotes, cancellationToken);
        var votesWritten = _pendingVotes.Count;
        _pendingVotes.Clear();

        var assignments = new List<PartyAssignment>(_pendingAssignments);
        _pendingAssignments.Clear();

        if (assignments.Count == 0)
        {
            return new FlushResult(votesWritten, 0, 0);
        }

        var inserted = await _assignmentRepository.UpsertBatchAsync(assignments, cancellationToken);
        var insertedIds = new HashSet<Guid>(inserted.Select(r => r.Id));

        var now = DateTimeOffset.UtcNow;
        var newRankOne = 0;

        foreach (var assignment in assignments)
        {
            if (!insertedIds.Contains(assignment.Id))
            {
                continue; // another run already claimed this candidate row — no side effects for us
            }

            writeContext.PartyAssignmentEvents.Add(new PartyAssignmentEvent
            {
                Id = Guid.NewGuid(),
                AssignmentId = assignment.Id,
                EventType = ScopeConstants.ProposedEventType,
                NewIdentityId = assignment.IdentityId,
                Actor = ScopeConstants.OwnershipWorkflowActor,
                RunId = _runIdText,
                OccurredAt = now,
            });

            if (assignment.Rank == 1)
            {
                newRankOne++;
            }
        }

        await writeContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Ownership flush for run {RunId}: {VotesWritten} votes staged, {NewAssignments}/{TotalStaged} assignments " +
            "newly inserted ({NewRankOne} new rank-1).",
            runId, votesWritten, inserted.Count, assignments.Count, newRankOne);

        return new FlushResult(votesWritten, inserted.Count, newRankOne);
    }
}
