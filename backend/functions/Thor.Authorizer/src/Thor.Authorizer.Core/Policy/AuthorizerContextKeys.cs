namespace Thor.Authorizer.Core.Policy;

/// <summary>
/// API Gateway authorizer context only supports flat string key-value pairs — no nested
/// objects or arrays — hence Scopes is serialized as a comma-delimited string.
/// </summary>
public static class AuthorizerContextKeys
{
    public const string TenantId = "tenant_id";
    public const string CallerType = "caller_type";
    public const string PrincipalId = "principal_id";
    public const string Scopes = "scopes";
}
