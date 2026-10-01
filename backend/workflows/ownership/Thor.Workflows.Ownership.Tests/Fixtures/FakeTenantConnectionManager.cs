using Npgsql;
using Thor.DataConnectionManager;
using Thor.DataLayer.Data;

namespace Thor.Workflows.Ownership.Tests.Fixtures;

/// <summary>
/// Hands every step the context the test chose, skipping Master-DB routing. <c>GetValidatedConnectionAsync</c>
/// throws rather than returning null because no Ownership step calls it — if one starts to, the test should
/// fail loudly instead of passing against a silent null.
/// </summary>
public sealed class FakeTenantConnectionManager(Func<Guid, TenantDbContext> factory) : ITenantConnectionManager
{
    public Task<NpgsqlConnection> GetValidatedConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TenantDbContext> GetTenantDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(factory(tenantId));
}
