using System.Security.Cryptography;
using System.Text;

namespace Thor.Auth;

/// <summary>
/// Hashes bearer secrets (connector API keys, refresh tokens) the way ADR §5.2 defines for
/// API keys — <c>HMAC-SHA256(pepper, salt ‖ secret)</c> with a per-credential random salt and
/// a global pepper — so issuance and verification always run the same code path.
/// </summary>
public static class SecretHasher
{
    public static string ComputeHash(string pepper, string salt, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(salt + secret));
        return Convert.ToHexString(hash);
    }

    public static bool Verify(string pepper, string salt, string secret, string expectedHash)
    {
        var actualHash = ComputeHash(pepper, salt, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualHash),
            Encoding.UTF8.GetBytes(expectedHash));
    }

    public static string GenerateSalt() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static string GenerateSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
