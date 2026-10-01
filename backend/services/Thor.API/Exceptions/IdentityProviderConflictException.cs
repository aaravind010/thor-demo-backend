namespace Thor.Api.Exceptions;

/// <summary>The tenant's user pool already has an identity provider with this name.</summary>
public sealed class IdentityProviderConflictException(string providerName)
    : Exception($"Identity provider '{providerName}' already exists.")
{
    public string ProviderName { get; } = providerName;
}
