namespace Thor.Workflows.Ingestion.Models;

/// <summary>
/// A deferred, unresolved reference from one entity to another, keyed by the target's
/// alias (§3.3/§5.3 of the AD connector ingestion design). <c>Dir</c> "out" means this
/// entity points at the target matched by <c>Key</c>; "in" means the target matched by
/// <c>Key</c> points at this entity. <c>Props</c>, when present, is a JSON-object string
/// carried onto the resolved edge's <c>props</c> column verbatim (e.g. a permission set) —
/// most relationship types have none.
/// </summary>
public sealed record EdgeRef(string Rel, string Dir, string Key, string? TargetType = null, string? Props = null);
