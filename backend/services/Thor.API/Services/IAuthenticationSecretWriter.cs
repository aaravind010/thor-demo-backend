namespace Thor.Api.Services;

/// <summary>
/// Writes every field value for one authentication-method creation into the tenant +
/// authentication-type secret in AWS Secrets Manager (see
/// docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md) in a single get-then-put, creating
/// that secret on first use. A request has exactly one authentication type, so it maps to
/// exactly one secret; batching every field into one write (rather than one write per field)
/// avoids the multiple round-trips that let a later write in the same request read a copy of
/// the secret that hadn't yet caught up with an earlier one (Secrets Manager does not
/// guarantee read-after-write consistency). Returns the secret's ARN, to be stored alongside
/// every <see cref="Thor.DataLayer.Models.Tenants.AuthenticationValue"/> row from the request.
/// </summary>
public interface IAuthenticationSecretWriter
{
    Task<string> StoreValuesAsync(
        Guid tenantId,
        Guid authenticationTypeId,
        IReadOnlyDictionary<Guid, string> valuesByAuthenticationValueId,
        CancellationToken cancellationToken = default);
}
