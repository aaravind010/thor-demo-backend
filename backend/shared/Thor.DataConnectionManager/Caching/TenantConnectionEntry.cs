using Npgsql;

namespace Thor.DataConnectionManager.Caching;

/// <summary>
/// A tenant's cached, ready-to-use <see cref="NpgsqlDataSource"/> (one stable pool, its IAM
/// token refreshed out-of-band by a periodic password provider) plus the database name the
/// validator checks. The data source owns the pool, so the cache disposes it on eviction.
/// </summary>
public sealed record TenantConnectionEntry(NpgsqlDataSource DataSource, string DatabaseName);
