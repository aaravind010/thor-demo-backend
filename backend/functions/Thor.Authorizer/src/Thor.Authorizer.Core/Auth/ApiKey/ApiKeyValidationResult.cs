namespace Thor.Authorizer.Core.Auth.ApiKey;

public sealed record ApiKeyValidationResult(
    bool IsValid,
    string TenantId,
    string PrincipalId,
    IReadOnlyList<string> Scopes,
    string? FailureReason)
{
    public static ApiKeyValidationResult Success(string tenantId, string principalId, IReadOnlyList<string> scopes) =>
        new(true, tenantId, principalId, scopes, null);

    public static ApiKeyValidationResult Failure(string reason) =>
        new(false, string.Empty, string.Empty, Array.Empty<string>(), reason);
}
