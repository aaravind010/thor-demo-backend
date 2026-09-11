using System.Collections.Concurrent;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Thor.Authorizer.Core.Auth.ApiKey;
using Thor.Authorizer.Core.Auth.Exceptions;

namespace Thor.Authorizer.Function;

public sealed class SaltProviderOptions
{
    /// <summary>The Secrets Manager secret ID/ARN holding the shared salt, read from environment
    /// configuration. Null/empty is a fail-closed misconfiguration, not defaulted.</summary>
    public string? SecretId { get; init; }

    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Fetches and caches the shared salt from AWS Secrets Manager. Must be registered as a
/// singleton in DI — a scoped/transient lifetime silently defeats caching across warm Lambda
/// invocations. Fails closed: a missing SecretId, or any Secrets Manager error, throws
/// SaltUnavailableException rather than returning a stale/empty salt as if it were valid.
/// The secret's SecretString is expected to be the base64-encoded salt bytes.
/// </summary>
public sealed class SecretsManagerSaltProvider : ISaltProvider
{
    private readonly IAmazonSecretsManager _client;
    private readonly SaltProviderOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    private const string CacheKey = "salt";

    public SecretsManagerSaltProvider(IAmazonSecretsManager client, SaltProviderOptions options, TimeProvider timeProvider)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<byte[]> GetSaltAsync()
    {
        var now = _timeProvider.GetUtcNow();

        if (_entries.TryGetValue(CacheKey, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Salt;
        }

        var salt = await FetchAsync();
        _entries[CacheKey] = new CacheEntry(salt, now + _options.Ttl);
        return salt;
    }

    private async Task<byte[]> FetchAsync()
    {
        if (string.IsNullOrEmpty(_options.SecretId))
        {
            throw new SaltUnavailableException("salt secret id is not configured");
        }

        GetSecretValueResponse response;
        try
        {
            response = await _client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = _options.SecretId });
        }
        catch (Exception ex)
        {
            throw new SaltUnavailableException("failed to fetch salt from Secrets Manager", ex);
        }

        if (string.IsNullOrEmpty(response.SecretString))
        {
            throw new SaltUnavailableException("salt secret has no string value");
        }

        try
        {
            return Convert.FromBase64String(response.SecretString);
        }
        catch (FormatException ex)
        {
            throw new SaltUnavailableException("salt secret value is not valid base64", ex);
        }
    }

    private sealed record CacheEntry(byte[] Salt, DateTimeOffset ExpiresAt);
}
