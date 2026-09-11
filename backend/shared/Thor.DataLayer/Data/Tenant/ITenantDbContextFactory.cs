using System.Data.Common;

namespace Thor.DataLayer.Data;

/// <summary>
/// Creates a <see cref="TenantDbContext"/> bound to a specific tenant's database, given
/// already-resolved connection parameters.
/// </summary>
public interface ITenantDbContextFactory
{
    TenantDbContext Create(TenantConnectionInfo connectionInfo);

    /// <summary>
    /// Wraps an already-open connection (e.g. one resolved, opened, and tenant-validated by a
    /// connection manager) in a <see cref="TenantDbContext"/>. When <paramref name="contextOwnsConnection"/>
    /// is <c>true</c> (the default), disposing the returned context also closes/disposes the
    /// connection — the normal EF Core disposal pattern callers expect.
    /// </summary>
    TenantDbContext Create(DbConnection connection, bool contextOwnsConnection = true);
}
