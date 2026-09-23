namespace Thor.Api.Exceptions;

/// <summary>No AuthenticationMethod with the given id exists in the tenant database.</summary>
public sealed class AuthenticationMethodNotFoundException(Guid authMethodId) : Exception($"Authentication method '{authMethodId}' was not found.")
{
    public Guid AuthMethodId { get; } = authMethodId;
}
