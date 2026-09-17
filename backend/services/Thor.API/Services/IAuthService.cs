using Thor.Api.Models;

namespace Thor.Api.Services;

/// <summary>
/// Exchanges a connector credential (API key or refresh token) for a scoped access token.
/// Lets RegisterController depend on the contract rather than a concrete auth flow.
/// </summary>
public interface IAuthService
{
    Task<TaskAuthResponse> RegisterAsync(string rawApiKey, string roleType, CancellationToken cancellationToken);

    Task<TaskAuthResponse> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken);
}
