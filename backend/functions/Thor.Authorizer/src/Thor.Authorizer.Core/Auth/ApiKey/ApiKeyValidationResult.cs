namespace Thor.Authorizer.Core.Auth.ApiKey;

public sealed record ApiKeyValidationResult(
    bool IsValid,
    string PrincipalId,
    IReadOnlyList<string> Scopes,
    string? FailureReason)
{
    public static ApiKeyValidationResult Success(string principalId, IReadOnlyList<string> scopes) =>
        new(true, principalId, scopes, null);

    public static ApiKeyValidationResult Failure(string reason) =>
        new(false, string.Empty, Array.Empty<string>(), reason);
}
