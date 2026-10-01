using Microsoft.Extensions.Logging;
using Thor.DataLayer.Models.Tenants;
using Thor.Rules.AccountType;
using Thor.Workflows.Atre.Voting;

namespace Thor.Workflows.Atre.Rules;

internal sealed record ParsedRule(Guid RuleId, Guid TargetAccountTypeId, decimal Weight, RuleNode Definition);

/// <summary>
/// Exception boundary around <see cref="RuleEvaluator"/>: a single malformed rule (bad JSON, bad
/// regex, unexpected shape) must never abort a run — it's treated as non-firing and logged, not
/// thrown. Mirrors the POC's <c>_eval_rule</c> being wrapped in a bare exception handler per rule.
/// </summary>
internal static class RuleFiringEngine
{
    /// <summary>Parses every rule's <c>rule_definition</c> once, up front. A malformed rule is skipped (logged), not fatal.</summary>
    public static IReadOnlyList<ParsedRule> Parse(IReadOnlyList<AccountTypeRule> rules, ILogger logger)
    {
        var parsed = new List<ParsedRule>(rules.Count);
        foreach (var rule in rules)
        {
            if (!AccountTypeRuleDefinition.TryParse(rule.RuleDefinition, out var node, out var error))
            {
                logger.LogWarning("Skipping account_type_rule {RuleId} ({RuleName}) — invalid rule_definition: {Error}", rule.Id, rule.RuleName, error);
                continue;
            }

            parsed.Add(new ParsedRule(rule.Id, rule.TargetAccountTypeId, rule.PrecisionScore, node));
        }

        return parsed;
    }

    /// <summary>Evaluates every parsed rule against one account. Never throws — a rule that does is treated as non-firing.</summary>
    public static IReadOnlyList<AccountVote> Evaluate(IReadOnlyList<ParsedRule> rules, Account account, ILogger logger)
    {
        var votes = new List<AccountVote>();
        foreach (var rule in rules)
        {
            bool fired;
            try
            {
                fired = RuleEvaluator.Evaluate(rule.Definition, account);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Rule {RuleId} threw while evaluating account {AccountId} — treating as non-firing.", rule.RuleId, account.Id);
                fired = false;
            }

            if (fired)
            {
                votes.Add(new AccountVote(rule.RuleId, rule.TargetAccountTypeId, rule.Weight));
            }
        }

        return votes;
    }
}
