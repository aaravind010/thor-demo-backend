namespace Thor.TenantProvisioning.Core.Abstractions;

/// <summary>
/// Applies <c>TenantDbContext</c>'s EF Core migrations to a newly-provisioned tenant
/// database and grants the tenant's read-only role visibility into the resulting schema.
/// AWS/Npgsql-agnostic so the step stays testable. Must be idempotent — re-running applies
/// only the migrations still missing and re-issues the (idempotent) grants.
/// </summary>
public interface ITenantSchemaMigrator
{
    Task MigrateAsync(TenantSchemaMigrationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What the caller supplies — the database and the two roles from <see cref="ITenantDatabaseProvisioner"/>.</summary>
public sealed record TenantSchemaMigrationRequest(string DatabaseName, string RwDbUser, string RoDbUser);
