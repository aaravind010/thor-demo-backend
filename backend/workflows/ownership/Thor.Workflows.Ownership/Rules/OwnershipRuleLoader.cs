using Microsoft.Extensions.Logging;
using Thor.DataLayer.Data;
using Thor.DataLayer.Repositories;
using Thor.Rules.Ownership;

namespace Thor.Workflows.Ownership.Rules;

/// <summary>One active <c>ownership_rule</c> row, deserialized and validated — safe to hand to <see cref="OwnershipRuleCompiler"/>.</summary>
internal sealed record ParsedOwnershipRule(Guid Id, string RuleName, string RuleType, string AppliesTo, decimal Weight, RuleDefinitionPayload Definition);

/// <summary>
/// Loads this run's rule catalog for one entity-type phase. A malformed or invalid row is skipped
/// and logged, never fatal — mirrors ATRE's <c>RuleFiringEngine.Parse</c> exception boundary. Under
/// this engine every candidate a phase produces always traces back to one of these rows, so there
/// is no "hardcoded default" fallback left anywhere.
/// </summary>
internal static class OwnershipRuleLoader
{
    public static async Task<IReadOnlyList<ParsedOwnershipRule>> LoadActiveAsync(
        TenantDbContext db, string appliesTo, ILogger logger, CancellationToken cancellationToken = default)
    {
        var rows = await new OwnershipRuleRepository(db).GetActiveAsync(appliesTo, cancellationToken);
        var parsed = new List<ParsedOwnershipRule>(rows.Count);

        foreach (var row in rows)
        {
            if (!OwnershipRuleDefinition.TryParse(row.RuleType, row.AppliesTo, row.RuleDefinition, out var payload, out var error))
            {
                logger.LogWarning("Skipping ownership_rule {RuleId} ({RuleName}) — invalid rule_definition: {Error}", row.Id, row.RuleName, error);
                continue;
            }

            parsed.Add(new ParsedOwnershipRule(row.Id, row.RuleName, row.RuleType, row.AppliesTo, row.PrecisionScore, payload));
        }

        return parsed;
    }
}
