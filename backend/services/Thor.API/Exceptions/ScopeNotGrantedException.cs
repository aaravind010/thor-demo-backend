namespace Thor.Api.Exceptions;

/// <summary>The API key is valid, but the requested role_type is not one of its granted scopes.</summary>
public sealed class ScopeNotGrantedException(string roleType) : Exception($"Role type '{roleType}' is not granted to this API key.")
{
    public string RoleType { get; } = roleType;
}
