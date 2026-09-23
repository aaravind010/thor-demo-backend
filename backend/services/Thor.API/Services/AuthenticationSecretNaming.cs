namespace Thor.Api.Services;

/// <summary>
/// The one place that defines the Secrets Manager secret name for a tenant + authentication
/// type (see docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md). Shared by
/// <see cref="AuthenticationSecretWriter"/> (what it writes to) and
/// <see cref="AuthenticationMethodService"/> (what it locks on), so the two can never drift
/// apart.
/// </summary>
internal static class AuthenticationSecretNaming
{
    public static string SecretName(Guid tenantId, Guid authenticationTypeId) =>
        $"tenant/{tenantId}/{authenticationTypeId}";
}
