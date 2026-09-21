namespace Thor.Api.Constants;

/// <summary>
/// Single place where Thor.API reads its environment variables. There are no fallback
/// defaults: every value must be set in the environment, or startup fails fast via
/// <see cref="RequireEnvironmentVariable"/>. Program.cs and other consumers reference these
/// values instead of calling Environment.GetEnvironmentVariable directly.
/// </summary>
public static class AppEnvironment
{
    public static readonly string MasterDbHost = RequireEnvironmentVariable("THOR_MASTERDB_HOST");

    public static readonly string MasterDbDatabase = RequireEnvironmentVariable("THOR_MASTERDB_DATABASE");

    public static readonly string MasterDbUser = RequireEnvironmentVariable("THOR_MASTERDB_USER");

    public static readonly string MasterDbPassword = RequireEnvironmentVariable("THOR_MASTERDB_PASSWORD");

    public static readonly int MasterDbPort = int.Parse(RequireEnvironmentVariable("THOR_MASTERDB_PORT"));

    public static readonly string JwtSigningKey = RequireEnvironmentVariable("THOR_TASKAPI_JWT_SIGNING_KEY");

    public static readonly string ApiKeyPepper = RequireEnvironmentVariable("THOR_API_KEY_PEPPER");

    public static readonly bool TenantDbUseSsl = bool.Parse(RequireEnvironmentVariable("THOR_DB_USE_SSL"));

    private static string RequireEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
        }

        return value;
    }
}
