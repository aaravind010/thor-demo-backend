namespace Thor.DataLayer.Data;

/// <summary>
/// Fully-resolved connection parameters for the Master metadata DB (see ADR §6.2).
/// Unlike <see cref="TenantConnectionInfo"/>, there is exactly one Master DB — callers
/// resolve these values once, from their own configuration, not per request.
/// </summary>
/// <remarks>
/// Authentication is always RDS IAM: there is no password anywhere. The factory mints a
/// short-lived IAM token at connection-open time using <see cref="Region"/>; nothing but a
/// db-user name and region is stored.
/// </remarks>
public sealed record MasterConnectionInfo(
    string Host,
    string Database,
    string Username,
    string Region,
    int Port = 5432,
    bool UseSsl = true)
{
    public string Host { get; init; } = Require(Host, nameof(Host));
    public string Database { get; init; } = Require(Database, nameof(Database));
    public string Username { get; init; } = Require(Username, nameof(Username));
    public string Region { get; init; } = Require(Region, nameof(Region));

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;
}
