using Microsoft.IdentityModel.Tokens;

namespace Thor.Auth;

public interface IJwksProvider
{
    Task<JsonWebKeySet> GetJwksAsync(string userPoolId, string region);
}
