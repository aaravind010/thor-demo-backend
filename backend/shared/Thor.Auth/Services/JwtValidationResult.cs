namespace Thor.Auth;

public sealed record JwtValidationResult(
    bool IsValid,
    string PrincipalId,
    IReadOnlyList<string> Scopes,
    string? FailureReason)
{
    public static JwtValidationResult Success(string principalId, IReadOnlyList<string> scopes) =>
        new(true, principalId, scopes, null);

    public static JwtValidationResult Failure(string reason) =>
        new(false, string.Empty, Array.Empty<string>(), reason);
}
