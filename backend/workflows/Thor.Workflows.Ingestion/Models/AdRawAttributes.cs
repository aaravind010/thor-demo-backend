namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// AD-specific attributes carried alongside a canonical account record, plus the
/// deferred-reference contract (§3.3) every entity carries: <c>AliasKeys</c> (how this
/// entity may be referenced) and <c>EdgeRefs</c> (references this entity carries to others).
/// </summary>
public sealed record AccountRawAttributes(
    string? Dn,
    IReadOnlyList<string> ObjectClass,
    string? Department,
    string? GivenName,
    string? Sn,
    string? EmployeeId,
    IReadOnlyList<string> AliasKeys,
    IReadOnlyList<EdgeRef> EdgeRefs);

/// <summary>
/// AD-specific attributes carried alongside a canonical group record (§5.2.3) — a smaller
/// set than <see cref="AccountRawAttributes"/> since AD's raw group records carry less.
/// </summary>
public sealed record GroupRawAttributes(
    string? Dn,
    IReadOnlyList<string> AliasKeys,
    IReadOnlyList<EdgeRef> EdgeRefs);
