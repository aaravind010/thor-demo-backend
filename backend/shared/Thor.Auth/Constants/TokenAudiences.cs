namespace Thor.Auth;

/// <summary>
/// JWT audience values (the <c>aud</c> claim) for tokens minted by a Thor.Auth issuer. Shared
/// by the issuing side (e.g. Thor.Api's connector registration flow) and the validating side
/// (e.g. <see cref="ThorTokenValidator"/>) so the two can never drift apart.
/// </summary>
public static class TokenAudiences
{
    /// <summary>Audience for tokens issued to authenticate against Thor.TaskApi (ADR §5.2).</summary>
    public const string TaskApi = "thor-task-api";
}
