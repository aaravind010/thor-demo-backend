namespace Thor.Authorizer.Core.DataAccess;

public sealed record ApiKeyRecord(
    string KeyId,
    string TenantId,
    string SecretHash,
    string StatusId,
    DateTimeOffset? ExpiresAt,
    string PrincipalId
);
