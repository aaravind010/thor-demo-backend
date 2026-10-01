namespace Thor.TaskApi.Services;

/// <summary>
/// Dev-only stand-in for <see cref="AuthenticationSecretReader"/>, registered only when the host
/// environment is Development. Thor.Api's own dev stand-in
/// (<c>LocalAuthenticationSecretWriter</c>) keeps written secrets in-memory inside its own
/// process, which this process (Thor.TaskApi) can never see, so there is no local data this
/// class could honestly return. It fails loudly instead of silently returning empty/fake values.
/// </summary>
public sealed class LocalAuthenticationSecretReader : IAuthenticationSecretReader
{
    public Task<IReadOnlyDictionary<Guid, string>> GetValuesAsync(string secretArn, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "LocalAuthenticationSecretReader has no local secret store to read from: Thor.Api's dev stand-in " +
            "writer keeps secrets in-memory in its own process, invisible to Thor.TaskApi. Exercising " +
            "GET /tasks/{taskId}/settings locally requires running Thor.TaskApi against real AWS Secrets Manager.");
}
