namespace Thor.TenantProvisioning.Core;

/// <summary>
/// Deterministic names for the AWS resources a tenant gets. Determinism is what makes
/// the Cognito steps idempotent — a re-run resolves the existing pool/group by the same
/// name instead of creating a duplicate.
/// </summary>
public static class ProvisioningNames
{
    public const string AdminGroup = "admins";

    public static string UserPool(string subdomain) => $"thor-{subdomain}";

    public static string AppClient(string subdomain) => $"thor-{subdomain}-app";
}
