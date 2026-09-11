using Npgsql;
using Thor.DataLayer.Data;

namespace Thor.DataConnectionManager;

/// <summary>
/// Entry point for getting a tenant-validated database connection or EF Core session, per
/// the tenant connection flow in ADR §6.3: resolve routing + credential, open a connection,
/// validate it targets the expected tenant database, fail closed on any mismatch.
/// </summary>
public interface ITenantConnectionManager
{
    /// <summary>
    /// Returns an open, tenant-validated <see cref="NpgsqlConnection"/>. Caller owns disposal.
    /// </summary>
    Task<NpgsqlConnection> GetValidatedConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a <see cref="TenantDbContext"/> wrapping an open, tenant-validated connection.
    /// Disposing the context also disposes the underlying connection.
    /// </summary>
    Task<TenantDbContext> GetTenantDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
