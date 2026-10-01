namespace Thor.Api.Models;

/// <summary>
/// Body of <c>POST v{version}/identity-providers</c>. <see cref="ProviderType"/> is "SAML" or "OIDC"
/// and selects which of <see cref="Saml"/> / <see cref="Oidc"/> must be set. <see cref="AttributeMapping"/>
/// maps user-pool attributes (e.g. "email") to IdP claims.
/// </summary>
public sealed record CreateTenantIdentityProviderRequest(
    string ProviderName,
    string ProviderType,
    SamlProviderSettings? Saml,
    OidcProviderSettings? Oidc,
    Dictionary<string, string>? AttributeMapping,
    List<string>? IdpIdentifiers);
