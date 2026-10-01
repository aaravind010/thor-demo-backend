namespace Thor.Api.Models;

/// <summary>
/// Body of <c>PUT v{version}/identity-providers/{providerName}</c>. The SAML/OIDC settings are
/// replaced in full (an OIDC client secret must be re-sent); <see cref="AttributeMapping"/> and
/// <see cref="IdpIdentifiers"/> are left unchanged when omitted. The name and type can't change.
/// </summary>
public sealed record UpdateTenantIdentityProviderRequest(
    string ProviderType,
    SamlProviderSettings? Saml,
    OidcProviderSettings? Oidc,
    Dictionary<string, string>? AttributeMapping,
    List<string>? IdpIdentifiers);
