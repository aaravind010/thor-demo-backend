namespace Thor.Authorizer.Core.Auth.ApiKey;

/// <summary>
/// Supplies the salt used for every API key hash/verify, fetched from an external secret store
/// (e.g. Secrets Manager) rather than generated per key.
///
/// Deliberate trade-off: this is a single salt shared across every key, not a per-key random
/// salt. That means two keys with the same secret value hash identically, and a single
/// precomputed rainbow table attacks every key at once instead of needing one table per key —
/// the exact protection a per-key random salt exists to provide. This was a explicit, confirmed
/// choice; see Pbkdf2ApiKeyHasher's doc comment and the README for the full trade-off.
/// </summary>
public interface ISaltProvider
{
    Task<byte[]> GetSaltAsync();
}
