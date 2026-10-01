using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AtreVoteRepository(TenantDbContext context)
    : Repository<AtreVote>(context), IAtreVoteRepository
{
    public async Task UpsertBatchAsync(IReadOnlyList<AtreVote> votes, CancellationToken cancellationToken = default)
    {
        if (votes.Count == 0)
        {
            return;
        }

        var sql = new StringBuilder("""
            INSERT INTO tenant.atre_vote (id, entity_id, rule_id, voted_for, vote_weight, run_id, created_at)
            VALUES

            """);
        var parameters = new List<NpgsqlParameter>();

        for (var i = 0; i < votes.Count; i++)
        {
            var v = votes[i];
            if (i > 0)
            {
                sql.Append(",\n");
            }

            sql.Append($"(@id{i}, @entity_id{i}, @rule_id{i}, @voted_for{i}, @vote_weight{i}, @run_id{i}, @created_at{i})");

            parameters.Add(new NpgsqlParameter($"id{i}", v.Id));
            parameters.Add(new NpgsqlParameter($"entity_id{i}", v.EntityId));
            parameters.Add(new NpgsqlParameter($"rule_id{i}", v.RuleId));
            parameters.Add(new NpgsqlParameter($"voted_for{i}", v.VotedFor));
            parameters.Add(new NpgsqlParameter($"vote_weight{i}", v.VoteWeight));
            parameters.Add(new NpgsqlParameter($"run_id{i}", v.RunId));
            parameters.Add(new NpgsqlParameter($"created_at{i}", v.CreatedAt));
        }

        sql.Append("\nON CONFLICT (entity_id, rule_id, run_id) DO NOTHING");

        await TenantSqlExec.ExecAsync(context, sql.ToString(), parameters, cancellationToken);
    }

    public Task<KeysetPage<AtreVote>> ListAsync(
        Guid? entityId, Guid? ruleId, string? runId, Guid? after, int limit,
        CancellationToken cancellationToken = default) =>
        context.AtreVotes
            .AsNoTracking()
            .Where(v => entityId == null || v.EntityId == entityId)
            .Where(v => ruleId == null || v.RuleId == ruleId)
            .Where(v => runId == null || v.RunId == runId)
            .ToKeysetPageAsync(v => v.Id, after, limit, cancellationToken);
}
