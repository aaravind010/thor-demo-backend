namespace Thor.Api.Models;

public sealed record CreateConnectorApiKeyRequest(string Scope, string? Label, int ExpiresInDays);
