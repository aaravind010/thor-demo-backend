using System.Collections.Concurrent;
using Npgsql;
using Thor.DataConnectionManager.Caching;
using Thor.DataConnectionManager.Routing;
using Thor.DataConnectionManager.Validation;
using Thor.DataLayer.Auth;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataConnectionManager;

public sealed class TenantConnectionManager(
    ITenantRoutingResolver routingResolver,
    IRdsIamTokenProvider tokenProvider,
    ITenantConnectionValidator validator,
    TenantConnectionCache cache,
    ITenantDbContextFactory tenantDbContextFactory) : ITenantConnectionManager
{
    // The tenant RDS Proxy listens on the standard PostgreSQL port; routing stores only the
    // proxy host, not a port.
    private const int ProxyPort = 5432;

    // The IAM token is valid ~15 min. Refresh it comfortably ahead of expiry; retry quickly on
    // a failed mint. The data source keeps ONE pool and swaps the password out-of-band, so opens
    // stay fast and connections are reused (no per-open token minting, no pool fragmentation).
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TokenRefreshFailureInterval = TimeSpan.FromSeconds(5);

    // Keyed by (tenant, backend PID) rather than PID alone: Postgres backend PIDs are
    // per-server and get reused across different clusters, so a bare PID cache would let
    // one tenant's already-validated PID vouch for a different tenant's unvalidated
    // connection to a different cluster. Validating once per physical connection (not per
    // logical acquisition) relies on the data source returning the same pooled connector — and
    // thus the same backend PID — on reuse.
    private readonly ConcurrentDictionary<(Guid TenantId, int ProcessId), byte> _validatedPhysicalConnections = new();

    public async Task<NpgsqlConnection> GetValidatedConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entry = await ResolveDataSourceAsync(tenantId, cancellationToken);

        var connection = await entry.DataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            if (_validatedPhysicalConnections.TryAdd((tenantId, connection.ProcessID), 0))
            {
                await validator.ValidateAsync(tenantId, connection, entry.DatabaseName, cancellationToken);
            }
        }
        catch
        {
            // A validation failure means the cached data source is pointing somewhere wrong
            // (e.g. the wrong database) — drop it so the next call rebuilds from fresh routing.
            // A plain open failure (transient network) never reaches here, so the pool survives.
            await cache.EvictAsync(tenantId);
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

    private async Task<TenantConnectionEntry> ResolveDataSourceAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (cache.TryGet(tenantId, out var cached))
        {
            return cached;
        }

        var routing = await routingResolver.ResolveAsync(tenantId, cancellationToken);
        var entry = new TenantConnectionEntry(BuildDataSource(routing), routing.DatabaseName);

        // Another thread may have raced us to build the same tenant's data source; keep the one
        // that won and dispose ours to avoid leaking its pool.
        var stored = cache.GetOrAdd(tenantId, entry);
        if (!ReferenceEquals(stored, entry))
        {
            await entry.DataSource.DisposeAsync();
        }

        return stored;
    }

    // The password is supplied by a periodic provider (a fresh IAM token, generated locally),
    // never embedded in the connection string — so the pool key is stable across token rotations.
    private NpgsqlDataSource BuildDataSource(TenantRouting routing)
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = routing.ClusterEndpoint,
            Port = ProxyPort,
            Database = routing.DatabaseName,
            Username = routing.DbUser,
            SslMode = SslMode.Require,
        }.ConnectionString;

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UsePeriodicPasswordProvider(
            (_, _) => new ValueTask<string>(
                tokenProvider.GenerateToken(routing.ClusterEndpoint, ProxyPort, routing.DbUser, routing.Region)),
            TokenRefreshInterval,
            TokenRefreshFailureInterval);

        return builder.Build();
    }
}
