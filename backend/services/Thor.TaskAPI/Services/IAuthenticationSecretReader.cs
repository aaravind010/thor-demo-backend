namespace Thor.TaskApi.Services;

/// <summary>
/// Reads decrypted authentication field values back out of the tenant + authentication-type
/// secret in AWS Secrets Manager (see
/// docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md) that
/// <c>Thor.Api</c>'s <c>IAuthenticationSecretWriter</c> wrote. Backs
/// <c>GET /tasks/{taskId}/settings</c>, which hands these values to the connector so it can
/// authenticate against its target system.
/// </summary>
public interface IAuthenticationSecretReader
{
    /// <summary>
    /// Returns every value in the secret identified by <paramref name="secretArn"/>, keyed by
    /// the <see cref="Thor.DataLayer.Models.Tenants.AuthenticationValue.Id"/> each value belongs
    /// to.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetValuesAsync(string secretArn, CancellationToken cancellationToken = default);
}
