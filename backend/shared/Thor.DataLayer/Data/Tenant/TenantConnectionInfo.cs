namespace Thor.DataLayer.Data;

/// <summary>
/// Fully-resolved connection parameters for a single tenant database. Callers are
/// responsible for resolving these values (tenant routing, Secrets Manager, RDS Proxy
/// endpoint, etc.) before constructing this — the data layer never resolves tenants itself.
/// </summary>
public sealed record TenantConnectionInfo(
    string Host,
    string Database,
    string Username,
    string Password,
    int Port = 5432,
    bool UseSsl = true)
{
    public string Host { get; init; } = Require(Host, nameof(Host));
    public string Database { get; init; } = Require(Database, nameof(Database));
    public string Username { get; init; } = Require(Username, nameof(Username));
    public string Password { get; init; } = Require(Password, nameof(Password));

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;
}
