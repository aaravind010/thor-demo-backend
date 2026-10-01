namespace Thor.Auth;

public sealed record JwtValidationResult(
    bool IsValid,
    string PrincipalId,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Groups,
    string? FailureReason)
{
    public static JwtValidationResult Success(
        string principalId, IReadOnlyList<string> scopes, IReadOnlyList<string>? groups = null) =>
        new(true, principalId, scopes, groups ?? Array.Empty<string>(), null);

    public static JwtValidationResult Failure(string reason) =>
        new(false, string.Empty, Array.Empty<string>(), Array.Empty<string>(), reason);
}
