namespace Thor.Api.Exceptions;

/// <summary>The tenant's user pool has no identity provider with this name.</summary>
public sealed class IdentityProviderNotFoundException(string providerName)
    : Exception($"Identity provider '{providerName}' was not found.")
{
    public string ProviderName { get; } = providerName;
}
