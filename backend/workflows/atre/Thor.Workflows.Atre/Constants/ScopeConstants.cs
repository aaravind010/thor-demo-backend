using Thor.Rules.AccountType;

namespace Thor.Workflows.Atre.Constants;

internal static class ScopeConstants
{
    /// <summary><see cref="Thor.DataLayer.Models.Tenants.AccountTypeRule.AppliesTo"/> / <see cref="Thor.DataLayer.Models.Tenants.AccountTypeAssignment.EntityType"/> value ATRE operates on.</summary>
    public const string AccountEntityType = AccountTypeRuleDefinition.AccountAppliesTo;

    /// <summary><see cref="Thor.DataLayer.Models.Tenants.AccountTypeAssignment.Method"/> value for a rule-engine decision (vs. "default"/"manual").</summary>
    public const string RuleMethod = "rule";

    /// <summary><see cref="Thor.DataLayer.Models.Tenants.AccountTypeAssignmentEvent.Actor"/> value for events ATRE writes.</summary>
    public const string AtreWorkflowActor = "atre_workflow";

    /// <summary><see cref="Thor.DataLayer.Models.Tenants.AccountTypeAssignmentEvent.EventType"/> value for a first-time classification.</summary>
    public const string AssignedEventType = "assigned";
}
