using System.Collections.Concurrent;
using Npgsql;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Secrets;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Data;

namespace Thor.DataConnectionManager;

public sealed class TenantConnectionManager(
    ITenantRoutingResolver routingResolver,
    ITenantSecretResolver secretResolver,
    ITenantConnectionValidator validator,
    TenantConnectionCache cache,
    ITenantDbContextFactory tenantDbContextFactory,
    TenantConnectionOptions options) : ITenantConnectionManager
{
    // Keyed by (tenant, backend PID) rather than PID alone: Postgres backend PIDs are
    // per-server and get reused across different clusters, so a bare PID cache would let
    // one tenant's already-validated PID vouch for a different tenant's unvalidated
    // connection to a different cluster. Validating once per physical connection (not per
    // logical acquisition) relies on OpenAsync returning the same pooled connector — and
    // thus the same backend PID — on reuse.
    private readonly ConcurrentDictionary<(Guid TenantId, int ProcessId), byte> _validatedPhysicalConnections = new();

    public async Task<NpgsqlConnection> GetValidatedConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entry = await ResolveConnectionAsync(tenantId, cancellationToken);

        var connection = new NpgsqlConnection(entry.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            if (_validatedPhysicalConnections.TryAdd((tenantId, connection.ProcessID), 0))
            {
                await validator.ValidateAsync(tenantId, connection, entry.DatabaseName, cancellationToken);
            }
        }
        catch
        {
            cache.Evict(tenantId);
            EvictValidatedPhysicalConnections(tenantId);
            await connection.DisposeAsync();
            throw;
        }

        return connection;
    }

    private void EvictValidatedPhysicalConnections(Guid tenantId)
    {
        foreach (var key in _validatedPhysicalConnections.Keys)
        {
            if (key.TenantId == tenantId)
            {
                _validatedPhysicalConnections.TryRemove(key, out _);
            }
        }
    }

    public async Task<TenantDbContext> GetTenantDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await GetValidatedConnectionAsync(tenantId, cancellationToken);
        return tenantDbContextFactory.Create(connection);
    }

    private async Task<TenantConnectionEntry> ResolveConnectionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (cache.TryGet(tenantId, out var cached))
        {
            return cached;
        }

        var routing = await routingResolver.ResolveAsync(tenantId, cancellationToken);
        var (username, password) = await secretResolver.ResolveAsync(routing.SecretArn, cancellationToken);

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = routing.ClusterEndpoint,
            Database = routing.DatabaseName,
            Username = username,
            Password = password,
            SslMode = options.UseSsl ? SslMode.Require : SslMode.Disable,
        }.ConnectionString;

        var entry = new TenantConnectionEntry(connectionString, routing.DatabaseName);
        cache.Set(tenantId, entry);

        return entry;
    }
}
