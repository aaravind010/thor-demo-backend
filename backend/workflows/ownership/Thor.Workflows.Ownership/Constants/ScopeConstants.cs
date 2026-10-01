using Thor.Rules.Ownership;

namespace Thor.Workflows.Ownership.Constants;

internal static class ScopeConstants
{
    /// <summary><see cref="Thor.DataLayer.Models.Tenants.OwnershipRule.AppliesTo"/> / <see cref="Thor.DataLayer.Models.Tenants.PartyAssignment.EntityType"/> value for accounts.</summary>
    public const string AccountEntityType = OwnershipEntityTypes.Account;

    /// <summary><see cref="Thor.DataLayer.Models.Tenants.OwnershipRule.AppliesTo"/> / <see cref="Thor.DataLayer.Models.Tenants.PartyAssignment.EntityType"/> value for groups.</summary>
    public const string GroupEntityType = OwnershipEntityTypes.Group;

    /// <summary><see cref="Thor.DataLayer.Models.Tenants.OwnershipRule.AppliesTo"/> / <see cref="Thor.DataLayer.Models.Tenants.PartyAssignment.EntityType"/> value for assets.</summary>
    public const string AssetEntityType = OwnershipEntityTypes.Asset;

    /// <summary>The three entity types Ownership proposes owners for, in the order each phase must complete before the next reads its results.</summary>
    public static readonly IReadOnlyList<string> EntityTypesInPhaseOrder = [AccountEntityType, GroupEntityType, AssetEntityType];

    /// <summary>Neptune vertex label for an owning identity.</summary>
    public const string IdentityVertexType = "identity";

    /// <summary>Neptune edge label from an account to its owning identity.</summary>
    public const string OwnedByRelType = "OWNED_BY";

    /// <summary><see cref="Thor.DataLayer.Models.Tenants.PartyAssignmentEvent.Actor"/> value for events Ownership writes.</summary>
    public const string OwnershipWorkflowActor = "ownership_workflow";

    /// <summary><see cref="Thor.DataLayer.Models.Tenants.PartyAssignmentEvent.EventType"/> value for a freshly-matched candidate.</summary>
    public const string ProposedEventType = "proposed";
}
