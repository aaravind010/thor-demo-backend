namespace Thor.DataLayer.Data;

/// <summary>
/// Fully-resolved connection parameters for the Master metadata DB (see ADR §6.2).
/// Unlike <see cref="TenantConnectionInfo"/>, there is exactly one Master DB — callers
/// resolve these values once, from their own configuration/Secrets Manager, not per request.
/// </summary>
public sealed record MasterConnectionInfo(
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
