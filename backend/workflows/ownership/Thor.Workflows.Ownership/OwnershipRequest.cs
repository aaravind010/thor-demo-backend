namespace Thor.Workflows.Ownership;

/// <summary>
/// One Ownership run's identity. Deserialized from the raw Lambda invocation payload.
/// <see cref="TenantId"/> is trusted as already-verified upstream, matching this repo's
/// <c>IngestionRequest</c> convention — the connection-level tenant validator still runs regardless.
///
/// <para>Two ways in, one record. Ingestion starts a run with a <see cref="ScanManifestId"/>: the run
/// covers only that manifest's changed entities and derives its <see cref="RunId"/> from it. A
/// caller with no manifest runs over every entity in the tenant; it must pass a <see cref="RunId"/>,
/// since there is nothing to derive one from, and may pass a <see cref="Scope"/> to say which
/// entities by current ownership state it wants re-matched (see
/// <see cref="Constants.OwnershipAssignmentScope"/>). <see cref="OwnershipScopes"/> says what an
/// absent scope means for each.</para>
///
/// <para>The window a vote invocation covers is not part of this record: the Distributed Map body
/// is invoked with a <see cref="Models.OwnershipChunkRequest"/>, which adds it.</para>
/// </summary>
public sealed record OwnershipRequest(Guid TenantId, Guid? ScanManifestId = null, Guid? RunId = null, string? Scope = null);
