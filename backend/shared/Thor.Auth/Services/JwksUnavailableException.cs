namespace Thor.Auth;

/// <summary>
/// Thrown when the JWKS for a user pool cannot be fetched or parsed. Callers must treat this
/// as a fail-closed condition (Deny) — never fall back to a stale or empty key set.
/// </summary>
public sealed class JwksUnavailableException : Exception
{
    public JwksUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
