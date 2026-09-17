namespace Thor.Auth;

/// <summary>
/// Validates a bearer token presented to a downstream service. One implementation per
/// audience/flow (e.g. <see cref="ThorTokenValidator"/> for tokens minted by
/// <see cref="ThorTokenIssuer"/> for Thor.TaskApi) — mirrors <see cref="ITokenIssuer"/>
/// on the verification side.
/// </summary>
public interface ITokenValidator
{
    TokenValidationResult Validate(string token);
}

/// <summary>
/// Outcome of <see cref="ITokenValidator.Validate"/>. When <see cref="IsValid"/> is false, the
/// caller must reject the request without trusting any other property here (fail closed).
/// </summary>
public sealed class TokenValidationResult
{
    /// <summary>A single shared instance for every failure — no failure carries extra state.</summary>
    public static readonly TokenValidationResult Invalid = new(false, default, default, string.Empty);

    public bool IsValid { get; }
    public Guid KeyId { get; }
    public Guid TenantId { get; }
    public string Scope { get; }

    private TokenValidationResult(bool isValid, Guid keyId, Guid tenantId, string scope)
    {
        IsValid = isValid;
        KeyId = keyId;
        TenantId = tenantId;
        Scope = scope;
    }

    public static TokenValidationResult Success(Guid keyId, Guid tenantId, string scope) =>
        new(true, keyId, tenantId, scope);
}
