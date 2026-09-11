namespace Thor.TenantProvisioning.Core.Abstractions;

/// <summary>
/// Creates a tenant's database and its read/write + read-only Postgres roles on the
/// existing Aurora cluster. The roles are IAM-auth (<c>GRANT rds_iam</c>) — no passwords,
/// no secrets. AWS/Npgsql-agnostic so the step stays testable. Must be idempotent — a
/// re-run resolves the existing database/roles rather than failing.
/// </summary>
public interface ITenantDatabaseProvisioner
{
    Task<TenantDatabaseResult> ProvisionAsync(TenantDatabaseRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What the caller supplies — enough to derive deterministic names.</summary>
public sealed record TenantDatabaseRequest(Guid TenantId, string Subdomain);

/// <summary>
/// What provisioning produced: the database name and the two IAM DB-user (role) names.
/// End-to-end IAM means there are no credential values or secret ARNs — the runtime mints
/// a short-lived RDS IAM token for these role names.
/// </summary>
public sealed record TenantDatabaseResult(string DatabaseName, string RwDbUser, string RoDbUser);
