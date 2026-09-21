using System.Collections.Concurrent;

namespace Thor.Api.Services;

/// <summary>
/// Dev-only stand-in for <see cref="AuthenticationSecretWriter"/>: keeps secret payloads
/// in-memory instead of calling AWS Secrets Manager. Registered only when the host environment
/// is Development.
/// </summary>
public sealed class LocalAuthenticationSecretWriter : IAuthenticationSecretWriter
{
    private readonly ConcurrentDictionary<string, AuthenticationSecretPayload> secrets = new();

    public Task<string> StoreValuesAsync(
        Guid tenantId,
        Guid authenticationTypeId,
        IReadOnlyDictionary<Guid, string> valuesByAuthenticationValueId,
        CancellationToken cancellationToken = default)
    {
        var secretName = AuthenticationSecretNaming.SecretName(tenantId, authenticationTypeId);

        var payload = secrets.GetOrAdd(secretName, _ => new AuthenticationSecretPayload());
        foreach (var (authenticationValueId, secretValue) in valuesByAuthenticationValueId)
        {
            payload.Values[authenticationValueId.ToString()] = secretValue;
        }

        return Task.FromResult($"arn:aws:secretsmanager:local:000000000000:secret:{secretName}");
    }
}
