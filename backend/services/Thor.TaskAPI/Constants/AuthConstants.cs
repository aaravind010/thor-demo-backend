namespace Thor.TaskApi.Constants;

/// <summary>Shared constants for the connector registration/refresh endpoints.</summary>
public static class AuthConstants
{
    public const string ApiKeyHeaderName = "x-task-api-key";

    public const string JwtIssuer = "thor-task-api";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
}
