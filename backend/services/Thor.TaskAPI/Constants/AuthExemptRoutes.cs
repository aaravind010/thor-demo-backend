namespace Thor.TaskApi.Constants;

/// <summary>Routes exempt from the connector JWT auth gate (see Program.cs).</summary>
public static class AuthExemptRoutes
{
    /// <summary>Path prefixes that bypass <c>TaskApiAuthMiddleware</c> entirely.</summary>
    public static readonly string[] PathPrefixes =
    [
        "/health",
        "/scalar",
        "/openapi",
    ];
}
