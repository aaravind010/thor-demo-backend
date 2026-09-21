namespace Thor.DataConnectionManager.Secrets;

/// <summary>
/// Resolves a tenant's database credential from AWS Secrets Manager, given the secret
/// ARN stored in the tenant's routing row. Credentials live only in Secrets Manager,
/// never in the Master metadata DB itself (see ADR §5.2/§8).
/// </summary>
public interface ITenantSecretResolver
{
    Task<(string Username, string Password)> ResolveAsync(string secretArn, CancellationToken cancellationToken = default);
}
