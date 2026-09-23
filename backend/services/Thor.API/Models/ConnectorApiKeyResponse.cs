namespace Thor.Api.Models;

// RawApiKey is only ever available here, in this one response: the database stores just its
// salted hash (ADR §5.2), so a caller that loses it must revoke the key and create a new one.
public sealed record ConnectorApiKeyResponse(
    Guid KeyId,
    string Scope,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string RawApiKey);
