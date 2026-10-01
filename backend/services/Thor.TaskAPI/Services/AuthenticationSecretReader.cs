using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Thor.DataConnectionManager.Secrets;

namespace Thor.TaskApi.Services;

public sealed class AuthenticationSecretReader(IAmazonSecretsManager secretsManager) : IAuthenticationSecretReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetValuesAsync(string secretArn, CancellationToken cancellationToken = default)
    {
        var response = await secretsManager.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = secretArn }, cancellationToken);

        var payload = JsonSerializer.Deserialize<AuthenticationSecretPayload>(response.SecretString)
            ?? throw new InvalidOperationException($"Secret '{secretArn}' did not contain a valid authentication payload.");

        return payload.Values.ToDictionary(kv => Guid.Parse(kv.Key), kv => kv.Value);
    }
}
