namespace Thor.Api.Services;

/// <summary>
/// Signing key + hashing secret for the connector registration flow. <paramref name="JwtPrivateKeyPem"/>
/// is the PEM-encoded RSA private key used to sign access tokens (RS256) — only Thor.Api holds
/// it; downstream validators (e.g. Thor.TaskApi) hold only the matching public key (ADR §5.2).
/// <paramref name="SecretPepper"/> is the global pepper from ADR §5.2 — shared by API-key and
/// refresh-token hashing.
/// </summary>
public sealed record ConnectorSecurityOptions(string JwtPrivateKeyPem, string SecretPepper);
