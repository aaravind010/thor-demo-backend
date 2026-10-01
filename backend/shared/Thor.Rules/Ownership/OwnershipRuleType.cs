namespace Thor.Rules.Ownership;

/// <summary>
/// Legal <c>OwnershipRule.RuleType</c> values. This is the
/// only discriminator the engine dispatches on — everything else about a rule (which fields,
/// which edge type, which weight, which entity type) is row content in
/// <c>OwnershipRule.RuleDefinition</c>, not code. Adding a new
/// rule that reuses one of these four types is a pure DB insert; a genuinely new shape of rule
/// still needs a new value here and a new compiler branch — kept to four because a majority of
/// realistic future rules (any "field X should also match", including substring/fuzzy variants)
/// fit inside <see cref="FieldMatch"/> as pure data.
/// </summary>
public static class OwnershipRuleType
{
    /// <summary>Correlate the entity (optionally after a hop/chain of hops) against a candidate identity.</summary>
    public const string FieldMatch = "field_match";

    /// <summary>Correlate the entity against another row of the *same* entity type, inheriting that sibling's owner.</summary>
    public const string SiblingMatch = "sibling_match";

    /// <summary>Take one related entity's (a single hop, or a walk up a hierarchy) already-active owner.</summary>
    public const string InheritOwner = "inherit_owner";

    /// <summary>Take the most common owner across many related entities reached via a hop chain.</summary>
    public const string MajorityOwner = "majority_owner";

    public static readonly IReadOnlyCollection<string> All = [FieldMatch, SiblingMatch, InheritOwner, MajorityOwner];
}
