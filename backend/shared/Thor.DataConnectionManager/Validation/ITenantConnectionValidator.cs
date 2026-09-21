using Npgsql;

namespace Thor.DataConnectionManager.Validation;

/// <summary>
/// Confirms an opened connection actually points at the expected tenant database before
/// it's used for anything else — the ADR §6.3 "Connection Interceptor" step. Fails closed
/// (throws) on any mismatch.
/// </summary>
public interface ITenantConnectionValidator
{
    Task ValidateAsync(Guid tenantId, NpgsqlConnection connection, string expectedDatabase, CancellationToken cancellationToken = default);
}
