namespace Thor.DataConnectionManager.Exceptions;

/// <summary>
/// The database a connection actually points at does not match the tenant's expected
/// database. Fail-closed signal per ADR §6.3 (§10 security event) — callers must not use
/// the connection.
/// </summary>
public sealed class TenantConnectionMismatchException(Guid tenantId, string expectedDatabase, string actualDatabase)
    : Exception($"Tenant '{tenantId}' expected database '{expectedDatabase}' but the connection resolved to '{actualDatabase}'.")
{
    public Guid TenantId { get; } = tenantId;
    public string ExpectedDatabase { get; } = expectedDatabase;
    public string ActualDatabase { get; } = actualDatabase;
}
