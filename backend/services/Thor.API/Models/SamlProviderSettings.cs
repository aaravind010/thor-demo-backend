namespace Thor.Api.Models;

/// <summary>SAML 2.0 IdP settings. Exactly one of <see cref="MetadataUrl"/> / <see cref="MetadataFile"/> is required.</summary>
public sealed record SamlProviderSettings(string? MetadataUrl, string? MetadataFile, bool? IdpSignout);
