namespace Thor.Api.Models;

/// <summary>
/// OIDC IdP settings. <see cref="ClientSecret"/> is passed straight to Cognito — Thor never
/// persists, logs, or returns it, so it must be re-sent on every update.
/// </summary>
public sealed record OidcProviderSettings(
    string ClientId,
    string ClientSecret,
    string Issuer,
    string AuthorizeScopes,
    string? AttributesRequestMethod);
