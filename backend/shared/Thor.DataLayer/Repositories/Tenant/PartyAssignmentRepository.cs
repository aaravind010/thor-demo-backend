using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class PartyAssignmentRepository(TenantDbContext context)
    : Repository<PartyAssignment>(context), IPartyAssignmentRepository
{
    public async Task<IReadOnlyList<PartyAssignmentUpsertResult>> UpsertBatchAsync(
        IReadOnlyList<PartyAssignment> assignments, CancellationToken cancellationToken = default)
    {
        if (assignments.Count == 0)
        {
            return [];
        }

        var sql = new StringBuilder("""
            INSERT INTO tenant.party_assignment
                (id, entity_type, entity_id, identity_id, rank, is_active, is_override,
                 vote_distribution, contributing_rule_ids, precision_score_snapshot, run_id, assigned_at)
            VALUES

            """);
        var parameters = new List<NpgsqlParameter>();

        for (var i = 0; i < assignments.Count; i++)
        {
            var a = assignments[i];
            if (i > 0)
            {
                sql.Append(",\n");
            }

            sql.Append(
                $"(@id{i}, @entity_type{i}, @entity_id{i}, @identity_id{i}, @rank{i}, @is_active{i}, @is_override{i}, " +
                $"@vote_distribution{i}, @contributing_rule_ids{i}, @precision_score_snapshot{i}, @run_id{i}, @assigned_at{i})");

            parameters.Add(new NpgsqlParameter($"id{i}", a.Id));
            parameters.Add(new NpgsqlParameter($"entity_type{i}", a.EntityType));
            parameters.Add(new NpgsqlParameter($"entity_id{i}", a.EntityId));
            parameters.Add(new NpgsqlParameter($"identity_id{i}", a.IdentityId));
            parameters.Add(new NpgsqlParameter($"rank{i}", a.Rank));
            parameters.Add(new NpgsqlParameter($"is_active{i}", a.IsActive));
            parameters.Add(new NpgsqlParameter($"is_override{i}", a.IsOverride));
            parameters.Add(new NpgsqlParameter($"vote_distribution{i}", a.VoteDistribution));
            parameters.Add(new NpgsqlParameter($"contributing_rule_ids{i}", a.ContributingRuleIds));
            parameters.Add(new NpgsqlParameter($"precision_score_snapshot{i}", a.PrecisionScoreSnapshot));
            parameters.Add(new NpgsqlParameter($"run_id{i}", a.RunId));
            parameters.Add(new NpgsqlParameter($"assigned_at{i}", a.AssignedAt));
        }

        sql.Append("\nON CONFLICT (entity_type, entity_id, identity_id, run_id) DO NOTHING");
        sql.Append("\nRETURNING id, entity_id, identity_id, rank");

        return await TenantSqlExec.QueryAsync(
            context, sql.ToString(),
            reader => new PartyAssignmentUpsertResult(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt32(3)),
            parameters, cancellationToken);
    }

    public Task<KeysetPage<PartyAssignment>> ListAsync(
        string? entityType, Guid? entityId, Guid? identityId, string? runId, bool? isActive, Guid? after, int limit,
        CancellationToken cancellationToken = default) =>
        context.PartyAssignments
            .AsNoTracking()
            .Where(a => entityType == null || a.EntityType == entityType)
            .Where(a => entityId == null || a.EntityId == entityId)
            .Where(a => identityId == null || a.IdentityId == identityId)
            .Where(a => runId == null || a.RunId == runId)
            .Where(a => isActive == null || a.IsActive == isActive)
            .ToKeysetPageAsync(a => a.Id, after, limit, cancellationToken);
}
