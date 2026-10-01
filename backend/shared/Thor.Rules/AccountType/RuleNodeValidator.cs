namespace Thor.Rules.AccountType;

/// <summary>
/// Validates that a deserialized <see cref="RuleNode"/> tree is unambiguous before it's trusted by
/// <c>RuleEvaluator</c>: each node must be exactly one of a compound <c>and</c>, a compound
/// <c>or</c>, or a leaf with a recognized operator — never both compound keys, and never neither.
/// Recurses into every child so one malformed node anywhere in the tree invalidates the whole rule.
/// </summary>
public static class RuleNodeValidator
{
    public static bool IsValid(RuleNode node, out string error)
    {
        var hasAnd = node.And is not null;
        var hasOr = node.Or is not null;
        var hasLeaf = !string.IsNullOrEmpty(node.Field);

        if (hasAnd && hasOr)
        {
            error = "node has both 'and' and 'or' — ambiguous.";
            return false;
        }

        if (hasAnd)
        {
            if (node.And!.Count == 0)
            {
                error = "node has an empty 'and' — no evaluable condition.";
                return false;
            }

            return AreValid(node.And!, out error);
        }

        if (hasOr)
        {
            if (node.Or!.Count == 0)
            {
                error = "node has an empty 'or' — no evaluable condition.";
                return false;
            }

            return AreValid(node.Or!, out error);
        }

        if (!hasLeaf)
        {
            error = "node has neither 'and'/'or' nor a 'field' — empty or malformed.";
            return false;
        }

        if (string.IsNullOrEmpty(node.Operator) || !RuleOperator.All.Contains(node.Operator))
        {
            error = $"unrecognized operator '{node.Operator}'.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool AreValid(List<RuleNode> children, out string error)
    {
        foreach (var child in children)
        {
            if (!IsValid(child, out error))
            {
                return false;
            }
        }

        error = "";
        return true;
    }
}
