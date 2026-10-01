namespace Thor.Workflows.Atre;

/// <summary>
/// One ATRE run's identity. Deserialized from the raw Lambda invocation payload (or <c>THOR_INPUT</c>
/// if a step is ever run on ECS). <see cref="TenantId"/> is trusted as already-verified upstream,
/// matching this repo's <c>IngestionRequest</c> convention — the connection-level tenant validator
/// still runs regardless.
///
/// <see cref="ScanManifestId"/> scopes the run to accounts referenced by that manifest's
/// <c>IngestChangeEvent</c> rows; omitting it falls back to a full-table scan of <c>account</c>
/// (see <see cref="Streaming.AccountStreamReader"/>). <see cref="RunId"/> lets a caller pin an
/// explicit idempotency key — required for retry-safety on the full-scan fallback, since there is
/// no manifest to derive one from (see <see cref="Persistence.AtreRunIdentity"/>).
///
/// <para>Which slice of the run a given invocation does is not part of this record: the Distributed
/// Map body is invoked with an <see cref="Models.AtreChunkRequest"/>, which adds the window. The two
/// are separate because every step reads this one, and only the Map body has a window.</para>
/// </summary>
public sealed record AtreRequest(Guid TenantId, Guid? ScanManifestId = null, Guid? RunId = null);
