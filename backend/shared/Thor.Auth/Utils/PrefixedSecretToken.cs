namespace Thor.Auth;

/// <summary>
/// Formats/parses bearer tokens of the shape <c>{prefix}_{id}_{secret}</c> — the connector
/// API-key format from ADR §5.2 (<c>thor_{keyId}_{secret}</c>), and reused here for the
/// same-shaped refresh token so a token's storage row can be found in O(1) instead of a
/// table scan over hashes.
/// </summary>
public static class PrefixedSecretToken
{
    public static string Format(string prefix, Guid id, string secret) => $"{prefix}_{id}_{secret}";

    public static bool TryParse(string token, string prefix, out Guid id, out string secret)
    {
        id = default;
        secret = string.Empty;

        // Split on the prefix boundary first (not a plain '_' split) because refresh-token
        // prefixes contain their own underscore (e.g. "thor_rt").
        var expectedStart = prefix + "_";
        if (!token.StartsWith(expectedStart, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = token[expectedStart.Length..].Split('_', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out id))
        {
            return false;
        }

        secret = parts[1];
        return secret.Length > 0;
    }
}
