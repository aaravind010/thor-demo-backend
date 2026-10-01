namespace Thor.Api.Constants;

/// <summary>Trusted headers set by the Lambda authorizer for tenant-scoped endpoints.</summary>
public static class TenantConstants
{
    public const string TenantHeaderName = "X-THOR-TENANT-ID";

    public const string ActorHeaderName = "X-THOR-ACTOR-ID";

    /// <summary>
    /// Comma-separated Cognito groups of the calling user, from the authorizer's verified context
    /// (API Gateway overwrites any client-sent value — see infra api_gateway/integration.tf).
    /// Empty for machine callers.
    /// </summary>
    public const string CallerGroupsHeaderName = "X-THOR-CALLER-GROUPS";

    /// <summary>
    /// Tenant admin group. Must match ProvisioningNames.AdminGroup in Thor.TenantProvisioning,
    /// which creates it and adds the tenant's first admin to it.
    /// </summary>
    public const string AdminGroup = "admins";
}
