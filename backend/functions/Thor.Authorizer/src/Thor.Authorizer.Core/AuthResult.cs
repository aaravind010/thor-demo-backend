namespace Thor.Authorizer.Core;

public sealed record AuthResult
{
    public bool IsAllowed { get; }
    public string TenantId { get; }
    public string PrincipalId { get; }
    public string CallerType { get; }
    public IReadOnlyList<string> Scopes { get; }
    public IReadOnlyList<string> Groups { get; }
    public string? DenyReason { get; }

    private AuthResult(
        bool isAllowed, string tenantId, string principalId, string callerType,
        IReadOnlyList<string> scopes, IReadOnlyList<string> groups, string? denyReason)
    {
        IsAllowed = isAllowed;
        TenantId = tenantId;
        PrincipalId = principalId;
        CallerType = callerType;
        Scopes = scopes;
        Groups = groups;
        DenyReason = denyReason;
    }

    /// <param name="groups">Cognito user-pool groups (<c>cognito:groups</c>); machine callers have none.</param>
    public static AuthResult Success(
        string tenantId, string principalId, string callerType, IReadOnlyList<string> scopes,
        IReadOnlyList<string>? groups = null) =>
        new(true, tenantId, principalId, callerType, scopes, groups ?? Array.Empty<string>(), denyReason: null);

    public static AuthResult Deny(string denyReason) =>
        new(false, tenantId: string.Empty, principalId: string.Empty, callerType: string.Empty,
            scopes: Array.Empty<string>(), groups: Array.Empty<string>(), denyReason);
}
