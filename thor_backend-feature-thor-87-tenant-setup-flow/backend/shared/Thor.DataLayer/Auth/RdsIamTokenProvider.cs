using Amazon;
using Amazon.RDS.Util;

namespace Thor.DataLayer.Auth;

/// <summary>
/// Generates RDS IAM auth tokens from the ambient AWS credential chain. Token generation is
/// a local signing operation (no network round-trip), so it is cheap to call per connection.
/// </summary>
public sealed class RdsIamTokenProvider : IRdsIamTokenProvider
{
    public string GenerateToken(string host, int port, string dbUser, string region) =>
        RDSAuthTokenGenerator.GenerateAuthToken(RegionEndpoint.GetBySystemName(region), host, port, dbUser);
}
