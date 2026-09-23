using System.Security.Cryptography;

namespace Thor.Authorizer.Core.Auth.ApiKey;

/// <summary>
/// PBKDF2-HMAC-SHA256 hasher. The salt comes from ISaltProvider (backed by Secrets Manager) and
/// is shared across every key rather than generated per key — see ISaltProvider's doc comment
/// for the security trade-off this implies. Because the salt is no longer per-record, it is not
/// stored in the encoded hash string; the format is
/// "PBKDF2-SHA256$iterations$base64(derivedKey)" so the iteration count can still be bumped for
/// newly-created keys without breaking verification of older keys.
/// </summary>
public sealed class Pbkdf2ApiKeyHasher : IApiKeyHasher
{
    private const string Prefix = "PBKDF2-SHA256";
    private const int DefaultIterations = 210_000;
    private const int KeySizeBytes = 32;

    private readonly ISaltProvider _saltProvider;

    public Pbkdf2ApiKeyHasher(ISaltProvider saltProvider) => _saltProvider = saltProvider;

    public async Task<string> HashAsync(string secret)
    {
        var salt = await _saltProvider.GetSaltAsync();
        var derived = Rfc2898DeriveBytes.Pbkdf2(secret, salt, DefaultIterations, HashAlgorithmName.SHA256, KeySizeBytes);
        return $"{Prefix}${DefaultIterations}${Convert.ToBase64String(derived)}";
    }

    public async Task<bool> VerifyAsync(string secret, string encodedHash)
    {
        var parts = encodedHash.Split('$');
        if (parts.Length != 3 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var salt = await _saltProvider.GetSaltAsync();
        var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
