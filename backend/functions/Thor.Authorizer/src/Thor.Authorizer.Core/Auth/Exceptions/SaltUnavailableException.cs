namespace Thor.Authorizer.Core.Auth.Exceptions;

/// <summary>
/// Thrown when the shared salt cannot be fetched or is misconfigured. Callers must treat this as
/// a fail-closed condition (Deny) — never fall back to hashing/verifying without a salt.
/// </summary>
public sealed class SaltUnavailableException : Exception
{
    public SaltUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
