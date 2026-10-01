namespace Thor.Workflows.Ownership.Constants;

/// <summary>
/// Legal values for how a candidate query scopes entities by their current
/// <see cref="Thor.DataLayer.Models.Tenants.PartyAssignment"/> state. "Proposed" means an active,
/// non-override assignment (what this engine itself writes); "confirmed" means an active,
/// overridden assignment (not written by any code today — reserved for a future human-confirm
/// action). Mirrors <see cref="Thor.Rules.Ownership.OwnershipRuleType"/>'s string-constant convention.
/// </summary>
internal static class OwnershipAssignmentScope
{
    /// <summary>No assignment-status filtering — every in-scope entity is a candidate regardless of current ownership.</summary>
    public const string All = "all";

    /// <summary>Only entities with no active assignment at all.</summary>
    public const string UnassignedOnly = "unassigned";

    /// <summary>Unassigned entities plus those with an active, non-override ("proposed") assignment.</summary>
    public const string UnassignedAndProposed = "unassigned_and_proposed";

    /// <summary>Unassigned entities plus those with an active, override ("confirmed") assignment — today's implicit default behavior.</summary>
    public const string UnassignedAndConfirmed = "unassigned_and_confirmed";
}
