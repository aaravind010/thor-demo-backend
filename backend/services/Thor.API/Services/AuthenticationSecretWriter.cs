using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace Thor.Api.Services;

// Callers must hold the Postgres advisory lock for this tenant+authentication-type
// (AuthenticationMethodService.CreateAsync) before calling this — see
// docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md. That lock is what makes the
// get-modify-put sequence below safe across concurrent requests, including from other
// Thor.Api/ECS tasks; this class holds no locking of its own.
public sealed class AuthenticationSecretWriter(
    IAmazonSecretsManager secretsManager, ILogger<AuthenticationSecretWriter> logger) : IAuthenticationSecretWriter
{
    // Guards the get-modify-put sequence below: without it, two requests writing to the same
    // tenant+authentication-type secret can race (both read the same version, second write
    // wins), silently dropping the first request's entries even though its own PutSecretValue
    // call succeeds. This only serializes writes within this process — see
    // docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md O-12 for the remaining
    // cross-instance gap. This instance is registered as a singleton (Program.cs), so this
    // dictionary is intentionally shared across all requests — it holds only one lock per
    // secret name, never secret values.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> secretLocks = new();

    public async Task<string> StoreValuesAsync(
        Guid tenantId,
        Guid authenticationTypeId,
        IReadOnlyDictionary<Guid, string> valuesByAuthenticationValueId,
        CancellationToken cancellationToken = default)
    {
        var secretName = $"tenant/{tenantId}/{authenticationTypeId}";
        var gate = secretLocks.GetOrAdd(secretName, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            AuthenticationSecretPayload payload;
            var secretExists = true;

            try
            {
                var existing = await secretsManager.GetSecretValueAsync(
                    new GetSecretValueRequest { SecretId = secretName }, cancellationToken);

                payload = JsonSerializer.Deserialize<AuthenticationSecretPayload>(existing.SecretString)
                    ?? new AuthenticationSecretPayload();
            }
            catch (ResourceNotFoundException)
            {
                payload = new AuthenticationSecretPayload();
                secretExists = false;
            }

            foreach (var (authenticationValueId, secretValue) in valuesByAuthenticationValueId)
            {
                payload.Values[authenticationValueId.ToString()] = secretValue;
            }

            var secretString = JsonSerializer.Serialize(payload);

            logger.LogDebug(
                "Merging {ValueCount} value(s) into {SecretState} secret {SecretName}",
                valuesByAuthenticationValueId.Count, secretExists ? "existing" : "new", secretName);

            if (secretExists)
            {
                var putResponse = await secretsManager.PutSecretValueAsync(
                    new PutSecretValueRequest { SecretId = secretName, SecretString = secretString }, cancellationToken);
                return putResponse.ARN;
            }

            var createResponse = await secretsManager.CreateSecretAsync(
                new CreateSecretRequest { Name = secretName, SecretString = secretString }, cancellationToken);
            return createResponse.ARN;
        }
        finally
        {
            gate.Release();
        }
    }
}
