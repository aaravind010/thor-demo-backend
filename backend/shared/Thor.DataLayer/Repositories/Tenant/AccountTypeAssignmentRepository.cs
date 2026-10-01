using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class AccountTypeAssignmentRepository(TenantDbContext context)
    : Repository<AccountTypeAssignment>(context), IAccountTypeAssignmentRepository
{
    public async Task<IReadOnlyList<Guid>> UpsertBatchAsync(
        IReadOnlyList<AccountTypeAssignment> assignments, CancellationToken cancellationToken = default)
    {
        if (assignments.Count == 0)
        {
            return [];
        }

        var sql = new StringBuilder("""
            INSERT INTO tenant.account_type_assignment
                (id, entity_type, entity_id, account_type_id, method, is_override,
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
                $"(@id{i}, @entity_type{i}, @entity_id{i}, @account_type_id{i}, @method{i}, @is_override{i}, " +
                $"@vote_distribution{i}, @contributing_rule_ids{i}, @precision_score_snapshot{i}, @run_id{i}, @assigned_at{i})");

            parameters.Add(new NpgsqlParameter($"id{i}", a.Id));
            parameters.Add(new NpgsqlParameter($"entity_type{i}", a.EntityType));
            parameters.Add(new NpgsqlParameter($"entity_id{i}", a.EntityId));
            parameters.Add(new NpgsqlParameter($"account_type_id{i}", a.AccountTypeId));
            parameters.Add(new NpgsqlParameter($"method{i}", a.Method));
            parameters.Add(new NpgsqlParameter($"is_override{i}", a.IsOverride));
            parameters.Add(new NpgsqlParameter($"vote_distribution{i}", a.VoteDistribution));
            parameters.Add(new NpgsqlParameter($"contributing_rule_ids{i}", a.ContributingRuleIds));
            parameters.Add(new NpgsqlParameter($"precision_score_snapshot{i}", a.PrecisionScoreSnapshot));
            parameters.Add(new NpgsqlParameter($"run_id{i}", a.RunId));
            parameters.Add(new NpgsqlParameter($"assigned_at{i}", a.AssignedAt));
        }

        sql.Append("\nON CONFLICT (entity_id, entity_type) DO NOTHING\nRETURNING entity_id");

        return await TenantSqlExec.QueryAsync(context, sql.ToString(), reader => reader.GetGuid(0), parameters, cancellationToken);
    }
}
