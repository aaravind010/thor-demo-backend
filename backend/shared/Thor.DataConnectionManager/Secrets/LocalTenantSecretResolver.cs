using Thor.DataConnectionManager.Constants;

namespace Thor.DataConnectionManager.Secrets;

/// <summary>
/// Dev-only stand-in for <see cref="TenantSecretResolver"/>: reads tenant DB credentials from
/// environment variables instead of calling AWS Secrets Manager. Registered only when the host
/// environment is Development.
/// </summary>
public sealed class LocalTenantSecretResolver : ITenantSecretResolver
{
    public Task<(string Username, string Password)> ResolveAsync(string secretArn, CancellationToken cancellationToken = default) =>
        Task.FromResult((AppEnvironment.TenantDbUser, AppEnvironment.TenantDbPassword));
}
