namespace Thor.Api.Constants;

/// <summary>Routes exempt from the Cognito auth gate (see Program.cs).</summary>
public static class AuthExemptRoutes
{
    /// <summary>Path prefixes that bypass <c>CognitoAuthMiddleware</c> entirely.</summary>
    public static readonly string[] PathPrefixes =
    [
        "/health",
        "/task-api",
        "/scalar",
        "/openapi",
    ];

    /// <summary>First path segment for the connector API-key registration flow (RegisterController — a different credential type, see ADR §5.2).</summary>
    public const string RegisterSegment = "register";

    /// <summary>First path segment for SPA login discovery (LoginConfigController) — called before sign-in, so there's no token yet.</summary>
    public const string LoginConfigSegment = "login-config";

    /// <summary>
    /// First path segment for connector API-key creation (ConnectorApiKeysController). TEMPORARY,
    /// UNAUTHENTICATED: exempt only until Cognito sign-in is up and running, then this exemption
    /// (and its check in Program.cs) MUST be removed so the route goes back behind Cognito.
    /// </summary>
    public const string ConnectorApiKeysSegment = "connector-api-keys";
}
