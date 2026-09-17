using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace Thor.DataConnectionManager.Secrets;

public sealed class TenantSecretResolver(IAmazonSecretsManager secretsManager) : ITenantSecretResolver
{
    public async Task<(string Username, string Password)> ResolveAsync(string secretArn, CancellationToken cancellationToken = default)
    {
        var response = await secretsManager.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = secretArn },
            cancellationToken);

        var credential = JsonSerializer.Deserialize<TenantDbCredential>(response.SecretString)
            ?? throw new InvalidOperationException($"Secret '{secretArn}' did not contain a valid credential payload.");

        return (credential.Username, credential.Password);
    }
}
