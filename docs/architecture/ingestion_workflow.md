# Ingestion Workflow

> Companion to `ADR.md` §12 (Ingestion & CDC) and to `attribute_mapping.md` (the field-by-field
> raw→staging reference for each connector). This document explains how
> `backend/workflows/Thor.Workflows.Ingestion` implements the ADR's ingestion/CDC decisions today.
> `attribute_mapping.md` goes one level deeper on per-connector column mappings; this doc covers
> pipeline shape and control flow.

## 1. What this module does

Takes one connector's raw export (a zip or JSON file already uploaded to S3) — today Active
Directory, CyberArk, or Windows local accounts — and turns it into:

1. Canonical `account`/`grp` rows in the tenant's Postgres database, kept in sync via
   content-hash CDC. Two more entity kinds, `asset` (e.g. a CyberArk safe) and `entitlement`,
   flow through the same pipeline shape, though no connector produces an `entitlement` yet
   (see §12).
2. Canonical `edge` rows representing relationships between entities (group membership,
   manager/reports-to, safe access grants, etc.), resolved from each entity's raw references.
3. Vertices and edges in the tenant's partition of the shared Neptune graph cluster — for
   accounts, groups, assets, and edges.

It's one of the "workflow modules" from ADR §16: an independent, additive, tenant-scoped,
idempotent unit plugging into a queue → Step Functions → compute → DLQ pattern.

## 2. Deployment shape

One Docker image, four entry points — not four separate services.

- `Program.cs` registers four named steps and hands them to
  `Thor.Workflows.Abstractions.WorkflowHost.RunAsync`.
- Each container invocation reads two env vars: `THOR_STEP` (which step to run) and
  `THOR_INPUT` (JSON payload, deserializes to `IngestionRequest`).
- The step's result maps to a process exit code (0/1); Step Functions drives retries/DLQ off
  that exit code — the step never talks to Step Functions directly.

```csharp
public sealed record IngestionRequest(
    Guid TenantId, string ExportLocation, Guid ScanId, Guid ScanManifestId, int BatchSeq = 0);
```

`TenantId` routes every step to the right tenant database via
`TenantConnectionManagerFactory.Build()` (the Master-DB tenant-routing chain, ADR §6.2/§6.3).
`ExportLocation` is just the bucket (e.g. `s3://thor-uploads-dev`, no prefix/path) — per-file paths
come from `ScanManifest.FileLocations` (§4). `IngestionRequest` carries no `SourceId`:
`ExtractAndStageStep` resolves `Source.ConnectorType` **per file**, parsed from that file's own
`FileLocations` entry via `Thor.S3.UploadKeyParser`, and passes it to `ConnectorNormalizerFactory`
(§5) — a manifest can span more than one connector (a scan can run several sources at once) even
though it's always scoped to exactly one tenant and one scan.

| `THOR_STEP` | Class | Purpose |
|---|---|---|
| `extract-stage` | `ExtractAndStageStep` | Resolve connector → normalize → stage |
| `promote` | `PromoteStep` | CDC diff + upsert staging → canonical, all four entity kinds |
| `graph-load-start` | `GraphLoadStartStep` | Resolve edges, kick off Neptune bulk load |
| `graph-load-poll` | `GraphLoadPollStep` | Poll the Neptune bulk-load job to completion |

## 3. End-to-end flow

```
S3 (connector export .zip / JSON)
   │
   ▼
extract-stage ── resolve Source.ConnectorType → normalizer (Ad/CyberArk/Windows)
   │                                                          │
   │                staging_account / staging_grp / staging_asset / staging_entitlement
   ▼
promote ── hash-diff upsert staging → canonical, emit ingest_change_event,
   │        delete staging rows, maintain entity_alias
   ▼
graph-load-start ── resolve deferred edge refs → tenant.edge
   │                 partition changed accounts/groups/edges into delete vs. load
   │                 write Gremlin bulk-load CSVs to S3, start Neptune bulk load job
   ▼
graph-load-poll ── poll Neptune until the load job reaches a terminal state
```

Each arrow is a separate container invocation (a separate Step Functions state), except
`graph-load-start`, which resolves edges and starts the graph load in one invocation.

**Identifiers threaded through every step:**
- `TenantId` — selects the tenant DB and namespaces every graph id.
- `ScanId` — one CDC scan run; change events, edges, and bulk-load jobs are scoped by it.
- `ScanManifestId` — one manifest (set of files) within a scan; staging rows and promoted
  entities are scoped by this id. Edge resolution creates its own `ScanManifest` row per run
  and tags edge change events with that id instead.
- `SourceId` — the connector source (e.g. one AD domain); part of the `(source_id, native_id)`
  natural key canonical tables upsert on. Not on `IngestionRequest` — `ExtractAndStageStep` resolves
  it per file (see §4); every staging/canonical row carries its own `SourceId` column, which is
  what `Stager`/`Promoter`/`EntityAlias` read from, not the trigger payload.
- `BatchSeq` — a per-file ordering tiebreaker, only relevant when two staged rows for the same
  natural key land in the same instant.

## 4. Extraction — `Extraction/`

`IExportSource`/`S3ExportSource` abstract where a connector's export comes from (`s3://` URIs) —
there's no discovery/listing step. `ExtractAndStageStep` looks up the `ScanManifest` by
`ScanManifestId` and reads exactly the files in its `FileLocations` (each entry a file's full S3
object key, written by whatever creates the manifest); `ExportLocation` supplies only the bucket
(e.g. `s3://thor-uploads-dev`, no prefix/path). Each identifier is built as
`s3://{bucket}/{fileLocation}` and read directly — a `FileLocations` entry that doesn't actually
exist in S3 surfaces as a natural `GetObject` failure that propagates, rather than a silent
shortfall.

Each `FileLocations` entry is also `Thor.S3.UploadKeyParser.TryParse`'d for its own
`TenantId`/`SourceId`/`ScanId` (`tenants/{tenantId}/uploads/{scanId}/{sourceId}/{fileName}`) — the
same key shape in
`UploadService.CreatePresignedUploadUrlAsync`;  a parsed `TenantId` that doesn't match the
tenant whose database is already open, or a parsed `ScanId` that doesn't match the loaded
manifest's own `ScanId`, throws (fail-closed; both checks are data-integrity checks, not security
boundaries, since the tenant DB connection and the manifest are already established/trusted by
that point). The parsed `SourceId` resolves `Source.ConnectorType` and thus which normalizer
runs — **per file**, not once for the whole run, since a manifest can span more than one connector
even though it's always scoped to one tenant and one scan. `ZipExportReader` unzips one archive and
returns its first real entry, skipping zip directory placeholders.

Extraction happens inside each normalizer, not as a separate pipeline stage:
`AdNormalizer`/`CyberArkNormalizer`/`WindowsNormalizer` each call their own extractor
(`AdExtractor`/`CyberArkExtractor`/`WindowsExtractor`) as their first step. All three share
`BomStripper` (strips a leading UTF-8 BOM) and `JsonRepair` (best-effort recovery for malformed
JSON — trailing commas/comments, and truncated exports cut off mid-write). `JsonRepair`
deliberately refuses to guess at an invalid token sitting inside an otherwise well-formed
structure; that case is reported as unrecoverable rather than repaired. Every recovery/drop is
counted and logged per run.

AD's export has one quirk the other two connectors don't: it's several top-level JSON documents
concatenated with no separator, so `AdExtractor` walks the byte stream and splits them manually
before parsing. It also normalizes three different top-level export shapes into one, and repairs
a double-encoded `AccountsPaths` field the same way. CyberArk and Windows are each a single clean
JSON document — they get the same BOM-strip/repair treatment but don't need AD's multi-document
splitting.

## 5. Normalization — `Normalization/`

Three `IConnectorNormalizer` implementations, dispatched by `ConnectorNormalizerFactory` on
`ConnectorTypes` (§11): `AdNormalizer`, `CyberArkNormalizer` (401), `WindowsNormalizer` (402).
Each has the signature `Normalize(byte[] rawExportBytes, Guid sourceId) → IngestBatch`. For the
field-by-field raw→staging mapping, see `attribute_mapping.md`.

Simple pass-through columns (`display_name`, `email`, etc.) are resolved from a per-connector,
per-entity-kind JSON config (`AttributeMapping/Config/*.json`) via `IAttributeMapProvider`,
instead of being hardcoded per field. Fields needing real transform logic
(`account_kind`, `is_disabled`, `group_class`, …) stay hardcoded in the normalizer. Content
hashing is likewise config-driven: `ContentHasher.Hash(fields, hashFields)` takes its field list
from the connector's `AttributeMap`.

Edges are never resolved during normalization — each entity only records deferred `EdgeRef`s
(relationship type, direction, and a key identifying the other side) and `AliasKeys` for itself;
resolution happens later, in the edge gate (§8).

- **Active Directory (308)** — splits each raw record by `objectClass` into an account or a
  group, drops records with no `objectGUID`. Produces `MEMBER_OF`/`REPORTS_TO`/`MANAGED_BY`
  edge refs. `IsDeleted` is always hardcoded `false` (§12).
- **CyberArk (401)** — produces privileged-credential accounts (`STORED_IN` edge to their safe),
  safe-permission-grant accounts/groups (`HAS_ACCESS` edge, carrying a JSON `Props` payload with
  the permission set), and `ParsedAsset` rows for safes. Produces no groups from credentials and
  no entitlements — safe-permission grants are modeled as edges, not entitlements (§12).
- **Windows local accounts (402)** — local users and local security groups per file server,
  `MEMBER_OF` edges keyed by `SamAccountName` instead of a DN. Produces no assets or
  entitlements.

## 6. Staging — `Staging/Stager.cs`

`Stager.StageAsync` bulk-inserts whichever of the batch's four entity-kind lists are non-empty
into `staging_account`/`staging_grp`/`staging_asset`/`staging_entitlement` (all EF *keyless*
entities — transient, not identity-bearing rows). Several staging columns are non-nullable, but
normalization can legitimately produce `null` (e.g. no `mail`) — the stager coalesces those to
`""` on the way in; `RawAttributes`/`ContentHash` keep the true value independently.

A fifth keyless table, `staging_edge`, exists but `Stager` never writes to it — only the edge
gate does (§8).

## 7. Promotion (CDC) — `Promotion/Promoter.cs`

The set-based comparison ADR §12 calls for: staged rows are hash-diffed against canonical in
bulk and upserted, one entity kind at a time, inside its own transaction. Roughly:

```sql
WITH deduped AS (
    SELECT DISTINCT ON (source_id, native_id) * FROM tenant.staging_account
    WHERE scan_manifest_id = @scan_manifest_id
    ORDER BY source_id, native_id, received_at DESC, batch_seq DESC
)
INSERT INTO tenant.account (...)
SELECT ... FROM deduped
ON CONFLICT (source_id, native_id) DO UPDATE SET ...
WHERE tenant.account.content_hash IS DISTINCT FROM excluded.content_hash
RETURNING id, ..., (xmax = 0) AS inserted
```

- `DISTINCT ON` dedups a natural key staged more than once in one manifest (e.g. present in two
  files), keeping the latest by `received_at`/`batch_seq`.
- The `WHERE content_hash IS DISTINCT FROM` guard means an unchanged row produces no row in
  `RETURNING` at all — no spurious insert/update event.
- `xmax = 0` distinguishes a fresh insert from an update, in one statement.

For every returned row, `Promoter` emits an `IngestChangeEvent` (the watermark downstream steps
read for "what changed this run") and replaces that entity's `entity_alias` rows from its
`AliasKeys` — this is what makes it findable by the edge gate. Then, in the same transaction, it
deletes the manifest's staging rows and commits — re-running `promote` for the same manifest is
a no-op the second time, since nothing is left to re-promote.

This same shape (`PromoteAccountsAsync`/`PromoteGroupsAsync`/`PromoteAssetsAsync`/
`PromoteEntitlementsAsync`) applies uniformly to all four entity kinds. `hash_version` is always
hardcoded to `0` (§12).

### 7.1 Edge-ref delta tracking — `EdgeGate/EdgeRefDeltaComputer.cs`

On every non-initial scan, `PromoteStep` calls `EdgeRefDeltaComputer.ComputeAsync` once per
entity kind, *before* that kind's own promote call overwrites canonical. It diffs the staged
`EdgeRefs` against canonical's current value and records one `tenant.edge_ref_delta` row per
ref added or removed. This is what lets the edge gate's incremental resolve (§8) work on just
what changed, instead of an owner's whole ref footprint. It's skipped entirely on the initial
scan, since canonical is empty and everything would look like an add.

## 8. Edge Gate — `EdgeGate/`

Turns each entity's deferred `EdgeRef`s into real `tenant.edge` rows, by joining every entity's
refs against every entity's `alias_keys`. This runs entirely as set-based SQL (temp tables and
CTEs) against Postgres — no in-memory lookup table is loaded.

`EdgeResolver` has two modes:
- **Full resolve** (initial scan): seeds every row from all four owner tables
  (`account`/`grp`/`asset`/`entitlement`) and resolves each one's entire current `EdgeRefs`.
- **Incremental resolve** (every later scan): resolves only the refs this manifest's
  `edge_ref_delta` rows (§7.1) recorded as added or removed, plus any previously-unresolved
  refs that just became resolvable (§8.1) — proportional cost to what actually changed, not to
  the whole graph.

Both modes then share the same promote/removal/cleanup logic: staged edges are upserted into
`tenant.edge` with the same hash-diffed-upsert shape `Promoter` uses elsewhere (an edge whose
refs are re-derived later can "resurrect" even if its content hash is unchanged), and an edge is
only marked deleted if re-deriving both its endpoints' current refs still fails to reproduce it
(conservative, both-sided). Edge content hashes use SHA-256 (not the xxHash entity hashes,
§11) and are computed identically in SQL and C# so CDC diffing can't silently drift between the
two.

Edge resolution tags its edge change events with the caller's own `ScanManifestId` — the same
stable id account/group promotion already tags its own change events with, never a separately
minted id — so a retry naturally rediscovers this run's edges the same way it already rediscovers
account/group changes (§10).

### 8.1 One-sided reference healing

Some relationship types are stored on both endpoints in the source data (`MEMBER_OF`) and never
need healing. Others are one-sided (`manager`/`managedBy`) — a ref can't resolve until its
target has been promoted, which might happen in a later manifest. `OneSidedRefIndex` parks
every non-two-sided ref in `tenant.one_sided_ref`; the incremental resolve checks whether any
parked ref's target just arrived and, if so, resolves it directly.

### 8.2 Conservative removal (incremental only)

A candidate removed edge is only actually marked deleted if re-checking both of its endpoints'
current refs still fails to reproduce it — not just because one side's ref disappeared this
manifest. This avoids flapping an edge deleted/re-added across manifests that each only see one
side of a two-sided relationship.

## 9. Graph Sync

Neptune has one write path: `Steps/GraphLoadStartStep.cs` + `GraphLoadPollStep.cs`, via S3
Gremlin-CSV bulk load. It touches accounts, groups, assets, and edges. Assets have no
`is_deleted` column and are never soft-deleted by `Promoter`, so — unlike accounts/groups —
an asset only ever appears on the "write to the bulk-load CSV" side, never the "delete now via a
direct Gremlin call" side.

`GraphLoadStartStep`:
1. **Idempotency guard** — if a bulk-load job for this `ScanId` is already `"started"`, returns
   without starting another.
2. Runs edge resolution (§8).
3. **Partitions** changed accounts/groups/assets/edges into "delete now via a direct Gremlin call"
   (soft-deleted entities and removed edges) vs. "write to the bulk-load CSV". Deletes and CSV
   uploads are parallelized (`Parallel.ForEachAsync`, max degree 4; `Task.WhenAll` for uploads).
4. Writes Gremlin CSVs to S3 and starts the Neptune bulk load, recording a `GraphBulkLoadJob`
   row. If there's nothing to load or delete, it skips starting a job.

`GremlinCsvWriter` validates that every vertex in a batch shares the same property key set
(throws otherwise); edge CSVs use the union of every edge's property keys, since edges can carry
heterogeneous props (e.g. only some carry CyberArk permission data).

`NeptuneBulkLoaderClient` retries transient failures (up to 3 attempts, exponential backoff
starting at 200ms) on a thrown exception or a 5xx response; a 4xx fails immediately as a
caller/config error.

`GraphLoadPollStep` polls the load job on an exponentially backed-off interval with jitter
(starts at `THOR_GRAPH_BULKLOAD_POLL_INTERVAL_SECONDS`, default 10s; doubles each poll up to a
6× cap; up to 20% jitter), bounded by `THOR_GRAPH_BULKLOAD_MAX_WAIT_SECONDS` (default 300s).
Completion with row errors, outright failure, or exceeding the wait bound all mark the job
`"failed"`/throw, mapping to a non-zero exit code so Step Functions can retry or escalate.

Vertex/edge ids follow one scheme (`{tenantId}:{entityType}:{id}`) with the tenant id baked
into the address itself — structurally, a caller can't address a vertex without a tenant id.

## 10. Idempotency & tenant isolation — concretely

- **Tenant isolation**: every step resolves its tenant DB context via `TenantId` alone, through
  the Master-DB routing chain (ADR §6.2/§6.3); Neptune isolation is structural (§9).
- **Promote idempotency**: re-running `promote` for the same manifest is a no-op — the same
  transaction that upserts also deletes the staging rows.
- **Edge-resolve idempotency**: full resolve is a no-op on re-run for the same reason. Incremental
  resolve consumes and deletes its `edge_ref_delta` rows in the same transaction it resolves
  from, so a naive re-run couldn't re-derive them — but edge resolution tags its
  `IngestChangeEvent` rows with the caller's own stable `scanManifestId` directly (the same id
  account/group promotion already tags its events with), instead of a separately-minted manifest
  id. So a `graph-load-start` retry that runs *after* the resolve transaction committed but
  *before* a bulk-load job was recorded (e.g. the S3/Neptune call itself failed) queries for edge
  events under that same `scanManifestId` again and still finds the edges the first attempt
  already committed to Postgres — no separate checkpoint or lookup table needed.
- **Graph-load idempotency**: guarded by the `"started"` job check per `ScanId`; a re-run
  coalesces onto the same vertex/edge ids rather than duplicating them.
- **Partially implemented**: ADR §12 describes an explicit `tenant + scanId + fileId` idempotency
  key. No such key exists at file granularity — what exists is idempotency at
  `tenant + scanId + scanManifestId` granularity (a manifest is a batch of files, not one file),
  via the mechanisms above. Whether true per-file idempotency is needed is still open.

## 11. Constants

- **`ConnectorTypes`**: `ActiveDirectory = 308`, `CyberArk = 401`, `Windows = 402`.
- **`RelTypes.TwoSided`**: `{ "MEMBER_OF" }` — the one relationship type stored reciprocally on
  both endpoints. Every other rel type (`REPORTS_TO`, `MANAGED_BY`, `STORED_IN`, `HAS_ACCESS`)
  is one-sided and depends on healing (§8.1) to resolve if its target arrives later.

## 12. Where things live

```
backend/workflows/Thor.Workflows.Ingestion/
├── Program.cs                        entry point — registers the 4 steps
├── IngestionRequest.cs                the one shared input contract
├── Steps/                             one class per THOR_STEP value
│   ├── ExtractAndStageStep.cs
│   ├── PromoteStep.cs
│   ├── GraphLoadStartStep.cs
│   └── GraphLoadPollStep.cs
├── Extraction/
│   ├── AdExtractor.cs                 multi-document + AccountsPaths repair
│   ├── CyberArkExtractor.cs           root-document repair only
│   ├── WindowsExtractor.cs            root + LocalADPaths repair
│   ├── JsonRepair.cs                  shared malformed-JSON recovery
│   ├── BomStripper.cs                 shared leading-BOM strip
│   └── Sources/                       IExportSource, S3ExportSource, ZipExportReader
├── Normalization/
│   ├── ConnectorNormalizerFactory.cs
│   ├── AdNormalizer.cs / CyberArkNormalizer.cs / WindowsNormalizer.cs
├── AttributeMapping/                  config-driven column maps (see attribute_mapping.md)
├── Staging/Stager.cs                  IngestBatch → staging_account/grp/asset/entitlement
├── Promotion/Promoter.cs              staging → canonical, CDC hash-diff upsert
├── EdgeGate/
│   ├── EdgeRefDeltaComputer.cs        staged vs. canonical edge_refs diff (§7.1)
│   ├── EdgeResolver.cs                edge_refs/edge_ref_delta × alias_keys → tenant.edge
│   ├── OwnerRefsSql.cs                shared owner-table/CTE/hash SQL fragments
│   ├── SqlExec.cs                     ADO.NET helpers against the ambient EF connection
│   └── OneSidedRefIndex.cs            park/heal one-sided refs (§8.1)
├── Hashing/ContentHasher.cs           xxHash128 (entities) / SHA-256 (edges)
├── Models/                            EdgeRef, ParsedAccount/Group/Asset/Entitlement, IngestBatch
├── Constants/                         RelTypes, ConnectorTypes
├── Composition/TenantConnectionManagerFactory.cs
├── Orchestration/ScanLifecycle.cs     Scan.ScanType lookup, manifest status updates
└── Dockerfile                         single image, all 4 steps
```

Shared dependencies: `backend/shared/Thor.DataLayer` (`TenantDbContext`, repositories, entity
models), `backend/shared/Thor.Graph` (graph vertex/edge stores, `GraphIds`, bulk-load CSV writer
and Neptune client), `backend/shared/Thor.S3`, `backend/shared/Thor.DataConnectionManager`
(tenant routing/secrets, ADR §6.2/§6.3).

See `docs/architecture/attribute_mapping.md` for the field-by-field raw→staging mapping per
connector.
