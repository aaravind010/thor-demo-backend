namespace Thor.DataConnectionManager;

/// <summary>
/// Runtime options for tenant DB connections opened by <see cref="TenantConnectionManager"/>.
/// UseSsl defaults to true (Aurora requires SSL); set to false only for local dev against a
/// non-SSL Postgres.
/// </summary>
public sealed record TenantConnectionOptions(bool UseSsl = true);
