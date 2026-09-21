namespace Thor.Api.Exceptions;

/// <summary>No AuthenticationType with the given id exists in the Master metadata DB.</summary>
public sealed class AuthenticationTypeNotFoundException(Guid typeId) : Exception($"Authentication type '{typeId}' was not found.")
{
    public Guid TypeId { get; } = typeId;
}
