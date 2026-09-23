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
}
