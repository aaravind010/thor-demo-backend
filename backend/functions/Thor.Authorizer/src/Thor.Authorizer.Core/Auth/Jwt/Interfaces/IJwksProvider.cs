using Microsoft.IdentityModel.Tokens;

namespace Thor.Authorizer.Core.Auth.Jwt;

public interface IJwksProvider
{
    Task<JsonWebKeySet> GetJwksAsync(string userPoolId, string region);
}
