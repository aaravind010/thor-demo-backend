using System.Text.Json;
using System.Text.RegularExpressions;
using Thor.DataLayer.Models.Tenants;
using Thor.Rules.AccountType;

namespace Thor.Workflows.Atre.Rules;

/// <summary>
/// Pure recursive evaluator over a rule's parsed definition tree. Mirrors the POC's
/// <c>_eval_rule</c> for leaf/operator semantics: both sides lowercased before comparison
/// (case-insensitive), <c>regex</c> is an unanchored substring search (<see cref="Regex.IsMatch(string, string)"/>,
/// not a full match), and an unknown operator resolves to <c>false</c> rather than throwing.
/// Deliberately deviates from the POC on empty <c>and</c>/<c>or</c>: rather than applying Python's
/// vacuous-truth <c>all([])</c>/<c>any([])</c> convention, <see cref="RuleNodeValidator"/> rejects an
/// empty <c>and</c>/<c>or</c> before a rule is ever parsed, so <see cref="Evaluate"/> never actually
/// sees one — the <c>.All()</c>/<c>.Any()</c> calls below only ever run over a non-empty list. Can
/// still throw on a
/// catastrophic-backtracking-adjacent edge the regex timeout doesn't cover or other unexpected
/// input — <see cref="RuleFiringEngine"/> is the exception boundary that guarantees a bad rule
/// never aborts a run.
/// </summary>
internal static class RuleEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public static bool Evaluate(RuleNode node, Account account)
    {
        if (node.And is { } andClauses)
        {
            return andClauses.All(clause => Evaluate(clause, account));
        }

        if (node.Or is { } orClauses)
        {
            return orClauses.Any(clause => Evaluate(clause, account));
        }

        return EvaluateLeaf(node, account);
    }

    private static bool EvaluateLeaf(RuleNode node, Account account)
    {
        var field = node.Field ?? "";
        var op = node.Operator ?? RuleOperator.Equal;
        var value = RawValueToString(node.Value).ToLowerInvariant();
        var fieldValue = (AccountFacts.Resolve(account, field) ?? "").ToLowerInvariant();

        return op switch
        {
            RuleOperator.Equal => fieldValue == value,
            RuleOperator.NotEquals => fieldValue != value,
            RuleOperator.Contains => fieldValue.Contains(value, StringComparison.Ordinal),
            RuleOperator.StartsWith => fieldValue.StartsWith(value, StringComparison.Ordinal),
            RuleOperator.EndsWith => fieldValue.EndsWith(value, StringComparison.Ordinal),
            RuleOperator.IsNull => string.IsNullOrEmpty(fieldValue),
            RuleOperator.IsNotNull => !string.IsNullOrEmpty(fieldValue),
            RuleOperator.Regex => TryRegexIsMatch(value, fieldValue),
            _ => false,
        };
    }

    private static string RawValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        _ => "",
    };

    private static bool TryRegexIsMatch(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.None, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return false; // invalid pattern — non-firing, not fatal
        }
        catch (RegexMatchTimeoutException)
        {
            return false; // catastrophic-backtracking guard
        }
    }
}
