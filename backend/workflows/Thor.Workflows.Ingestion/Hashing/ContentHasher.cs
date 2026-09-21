using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Thor.Workflows.Ingestion.Hashing;

/// <summary>
/// Computes the CDC content hash for account/group records.
///
/// Entity hashes below only ever get compared against the same record's own previous
/// hash to detect a change — there's no adversary, so SHA-256's collision-resistance is
/// wasted CPU. They use xxHash (xxh3-128) instead: a fast non-cryptographic hash, 128-bit
/// so accidental collisions stay negligible. <see cref="EdgeHash"/> is the exception —
/// see its remarks.
/// </summary>
public static class ContentHasher
{
    /// <summary>
    /// Hashes <paramref name="hashFields"/> (an attribute map's <c>hashFields</c> list, in
    /// order) looked up out of <paramref name="fields"/>. Which fields are hashed is entirely
    /// caller-driven — this method has no per-entity-type knowledge, so a connector's attribute
    /// map can add/remove/reorder hashed fields without a code change.
    /// </summary>
    public static string Hash(IReadOnlyDictionary<string, object?> fields, IReadOnlyList<string> hashFields) =>
        XxHash(hashFields.Select(f => fields.GetValueOrDefault(f)).ToArray());

    /// <summary>
    /// Content hash for a resolved edge (§7's edge gate) — not part of the ingestor
    /// contract's §3.4 (that's for account/group entities), so not versioned alongside it.
    /// Stays on SHA-256 rather than xxHash used by the entity hashes above.
    ///
    /// Byte-for-byte pinned to the SQL expression <see cref="EdgeGate.EdgeResolver"/> now uses to
    /// compute this same hash in Postgres (so a row is never round-tripped into C# just to be
    /// hashed) — see <c>EdgeHashParityTests</c>. <paramref name="props"/> is embedded as raw JSON
    /// text, not re-escaped by <see cref="JsonSerializer"/>, and <c>"{}"</c> stands in for absent
    /// props — both to match Postgres's <c>'[".." ] || COALESCE(props, '{}') || ']'</c>
    /// string-concatenation, not JSON serialization. <c>edge</c>/<c>staging_edge.props</c> are
    /// plain TEXT columns (never cast to <c>jsonb</c>) specifically so this never has to reproduce
    /// jsonb's object-key reordering to stay in sync.
    /// </summary>
    public static string EdgeHash(Guid fromId, Guid toId, string relType, string? props)
    {
        var propsText = string.IsNullOrEmpty(props) ? "{}" : props;
        var payload = $"[\"{fromId}\", \"{toId}\", \"{relType}\", {propsText}]";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(digest);
    }

    private static string XxHash(params object?[] orderedFields)
    {
        var json = JsonSerializer.Serialize(orderedFields);
        var digest = XxHash128.Hash(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(digest);
    }
}
