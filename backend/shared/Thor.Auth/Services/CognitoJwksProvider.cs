using System.Collections.Concurrent;
using Microsoft.IdentityModel.Tokens;

namespace Thor.Auth;

public sealed class JwksProviderOptions
{
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Fetches and caches the JWKS for a Cognito user pool. Must be registered as a singleton in
/// DI — a scoped/transient lifetime silently defeats caching across warm Lambda invocations.
/// Fails closed: any HTTP or parse failure throws JwksUnavailableException rather than
/// returning a stale/empty key set as if it were valid.
/// </summary>
public sealed class CognitoJwksProvider : IJwksProvider
{
    private const string HttpClientName = "CognitoJwks";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JwksProviderOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public CognitoJwksProvider(IHttpClientFactory httpClientFactory, JwksProviderOptions options, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<JsonWebKeySet> GetJwksAsync(string userPoolId, string region)
    {
        var now = _timeProvider.GetUtcNow();

        if (_entries.TryGetValue(userPoolId, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Jwks;
        }

        var jwks = await FetchAsync(userPoolId, region);
        _entries[userPoolId] = new CacheEntry(jwks, now + _options.Ttl);
        return jwks;
    }

    private async Task<JsonWebKeySet> FetchAsync(string userPoolId, string region)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}/.well-known/jwks.json";

        string json;
        try
        {
            json = await client.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            throw new JwksUnavailableException($"failed to fetch JWKS for user pool {userPoolId}", ex);
        }

        try
        {
            return new JsonWebKeySet(json);
        }
        catch (Exception ex)
        {
            throw new JwksUnavailableException($"failed to parse JWKS for user pool {userPoolId}", ex);
        }
    }

    private sealed record CacheEntry(JsonWebKeySet Jwks, DateTimeOffset ExpiresAt);
}
