namespace Thor.TenantProvisioning.Core.Models;

/// <summary>
/// The state passed between every step of the tenant-provisioning Step Functions
/// workflow. Each Lambda receives the whole state, performs its one step, and returns
/// the state enriched with what it produced (via <c>with</c>). Step Functions carries
/// it forward unchanged otherwise.
/// </summary>
/// <remarks>
/// Inputs are the tenant's onboarding details (supplied at StartExecution). Everything
/// else is produced by the steps — the tenant database, its IAM DB-user (role) names, and
/// the Cognito ids. Cluster endpoint and region are environment config (not per-tenant),
/// so they are injected into the steps that need them, not carried here. There are no DB
/// credentials anywhere: the roles are IAM-auth, and the Cognito admin password comes from
/// the invite flow.
/// </remarks>
public sealed record ProvisioningState
{
    // --- inputs ---
    public required string DisplayName { get; init; }

    public required string Subdomain { get; init; }

    public required string AdminEmail { get; init; }

    public short TierId { get; init; }

    public short IsolationTypeId { get; init; }

    // --- produced by the steps ---
    public Guid? TenantId { get; init; }

    public string? DatabaseName { get; init; }

    public string? RwDbUser { get; init; }

    public string? RoDbUser { get; init; }

    public string? UserPoolId { get; init; }

    public string? AppClientId { get; init; }
}
