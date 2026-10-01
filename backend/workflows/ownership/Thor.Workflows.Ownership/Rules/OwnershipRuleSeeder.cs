using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Thor.DataLayer.Data;
using Thor.Rules.Ownership;

namespace Thor.Workflows.Ownership.Rules;

/// <summary>
/// Idempotently populates a tenant's <c>ownership_rule</c> table with the built-in defaults on
/// first use. Insert-only, never <c>DO UPDATE</c> — the entire point of this engine is that an
/// operator can edit a rule's <c>rule_definition</c>/<c>precision_score</c>/<c>is_active</c>
/// directly in the DB with no redeploy, so seeding must never overwrite a row that already exists,
/// on this run or any future one. Cheap to call on every invocation: after the first run for a
/// tenant this is a single indexed <c>ON CONFLICT DO NOTHING</c> that inserts nothing.
/// </summary>
internal static class OwnershipRuleSeeder
{
    public static async Task EnsureSeededAsync(TenantDbContext db, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var seeds = OwnershipRuleDefaults.All;

        var sql = new System.Text.StringBuilder("""
            INSERT INTO tenant.ownership_rule
                (id, rule_name, rule_type, rule_definition, applies_to, is_active,
                 total_predictions, true_positive_count, false_positive_count, precision_score,
                 created_at, updated_at)
            VALUES

            """);
        var parameters = new List<NpgsqlParameter>();

        for (var i = 0; i < seeds.Count; i++)
        {
            var seed = seeds[i];
            if (i > 0)
            {
                sql.Append(",\n");
            }

            sql.Append(
                $"(@id{i}, @rule_name{i}, @rule_type{i}, @rule_definition{i}, @applies_to{i}, @is_active{i}, " +
                $"0, 0, 0, @precision_score{i}, @created_at{i}, @updated_at{i})");

            parameters.Add(new NpgsqlParameter($"id{i}", Guid.NewGuid()));
            parameters.Add(new NpgsqlParameter($"rule_name{i}", seed.RuleName));
            parameters.Add(new NpgsqlParameter($"rule_type{i}", seed.RuleType));
            parameters.Add(new NpgsqlParameter($"rule_definition{i}", seed.RuleDefinitionJson));
            parameters.Add(new NpgsqlParameter($"applies_to{i}", seed.AppliesTo));
            parameters.Add(new NpgsqlParameter($"is_active{i}", seed.IsActive));
            parameters.Add(new NpgsqlParameter($"precision_score{i}", seed.PrecisionScore));
            parameters.Add(new NpgsqlParameter($"created_at{i}", now));
            parameters.Add(new NpgsqlParameter($"updated_at{i}", now));
        }

        sql.Append("\nON CONFLICT (rule_name) DO NOTHING");

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            await using var command = new NpgsqlCommand(sql.ToString(), connection, transaction);
            command.Parameters.AddRange(parameters.ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
