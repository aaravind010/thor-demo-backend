namespace Thor.Auth;

/// <summary>
/// JWT issuer values (the <c>iss</c> claim) for tokens minted by a Thor.Auth issuer. Shared by
/// the issuing side (e.g. Thor.Api's connector registration flow) and the validating side (e.g.
/// <see cref="ThorTokenValidator"/> and the Lambda authorizer's issuer-based dispatch) so the two
/// can never drift apart.
/// </summary>
public static class TokenIssuers
{
    /// <summary>Issuer for tokens minted by Thor.Api's connector registration flow (ADR §5.2).</summary>
    public const string ThorTaskApi = "thor-task-api";
}
