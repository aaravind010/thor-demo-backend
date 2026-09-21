namespace Thor.DataConnectionManager.Constants;

/// <summary>
/// Single place where Thor.DataConnectionManager reads its environment variables.
/// </summary>
public static class AppEnvironment
{
    public static readonly string TenantDbUser = Environment.GetEnvironmentVariable("THOR_TENANTDB_USER") ?? "postgres";

    public static readonly string TenantDbPassword = Environment.GetEnvironmentVariable("THOR_TENANTDB_PASSWORD") ?? "postgres";
}
