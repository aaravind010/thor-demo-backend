namespace Thor.Authorizer.Core;

public sealed record AuthResult
{
    public bool IsAllowed { get; }
    public string TenantId { get; }
    public string PrincipalId { get; }
    public string CallerType { get; }
    public IReadOnlyList<string> Scopes { get; }
    public string? DenyReason { get; }

    private AuthResult(bool isAllowed, string tenantId, string principalId, string callerType, IReadOnlyList<string> scopes, string? denyReason)
    {
        IsAllowed = isAllowed;
        TenantId = tenantId;
        PrincipalId = principalId;
        CallerType = callerType;
        Scopes = scopes;
        DenyReason = denyReason;
    }

    public static AuthResult Success(string tenantId, string principalId, string callerType, IReadOnlyList<string> scopes) =>
        new(true, tenantId, principalId, callerType, scopes, denyReason: null);

    public static AuthResult Deny(string denyReason) =>
        new(false, tenantId: string.Empty, principalId: string.Empty, callerType: string.Empty, scopes: Array.Empty<string>(), denyReason);
}
