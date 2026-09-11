namespace Thor.TenantProvisioning.Function.Aws;

/// <summary>
/// Configuration for creating tenant databases. Points at the Aurora <b>writer</b> endpoint
/// (DDL must hit the writer, not the proxy/reader). Auth is always RDS IAM — a short-lived
/// token for <see cref="ProvisioningUser"/>; there is no password.
/// </summary>
public sealed record TenantDbProvisioningOptions
{
    public required string WriterEndpoint { get; init; }

    public required string Region { get; init; }

    public string ProvisioningUser { get; init; } = "thor_provisioner";

    /// <summary>Maintenance DB to connect to for <c>CREATE DATABASE</c> (which can't run inside the target DB).</summary>
    public string AdminDatabase { get; init; } = "postgres";

    public int Port { get; init; } = 5432;
}
