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
    public async Task<string> StoreValuesAsync(
        Guid tenantId,
        Guid authenticationTypeId,
        IReadOnlyDictionary<Guid, string> valuesByAuthenticationValueId,
        CancellationToken cancellationToken = default)
    {
        var secretName = AuthenticationSecretNaming.SecretName(tenantId, authenticationTypeId);

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

        logger.LogDebug(
            "Merging {ValueCount} value(s) into {SecretState} secret {SecretName}",
            valuesByAuthenticationValueId.Count, secretExists ? "existing" : "new", secretName);

        foreach (var (authenticationValueId, secretValue) in valuesByAuthenticationValueId)
        {
            payload.Values[authenticationValueId.ToString()] = secretValue;
        }

        var secretString = JsonSerializer.Serialize(payload);

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
}
