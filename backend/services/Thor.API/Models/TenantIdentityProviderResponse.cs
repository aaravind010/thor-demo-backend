namespace Thor.Api.Models;

/// <summary>
/// A tenant IdP as returned by the API. Built from an allow-list of non-secret settings — the
/// OIDC client secret Cognito returns on describe is never copied here.
/// </summary>
public sealed record TenantIdentityProviderResponse(
    string ProviderName,
    string ProviderType,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    SamlProviderSettingsResponse? Saml,
    OidcProviderSettingsResponse? Oidc,
    IReadOnlyDictionary<string, string> AttributeMapping,
    IReadOnlyList<string> IdpIdentifiers);

public sealed record SamlProviderSettingsResponse(string? MetadataUrl, bool? IdpSignout);

public sealed record OidcProviderSettingsResponse(
    string? ClientId, string? Issuer, string? AuthorizeScopes, string? AttributesRequestMethod);
