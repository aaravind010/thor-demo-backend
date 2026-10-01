namespace Thor.Workflows.Atre.Voting;

/// <summary>One rule's vote for one account: a target type weighted by the rule's precision score.</summary>
internal readonly record struct AccountVote(Guid RuleId, Guid TargetAccountTypeId, decimal Weight);
