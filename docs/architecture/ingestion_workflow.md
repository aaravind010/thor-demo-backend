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

One Docker image, four entry points — not four separate services. Every step supports running
either as an ECS task or as a Lambda function, sharing the same compiled binary as the
ECS/console image — which of the two backs any given step is a deployment-time choice, made
independently per step, not a capability specific to any one of them.

**One exception**: whichever entry point backs `graph-load-poll` specifically constrains how its
wait/re-poll loop can be implemented. `GraphLoadPollStep` reports "still in progress" as a distinct
outcome (`GraphLoadPollResult.IsInProgress`, mapped to `WorkflowHost.RetryLaterExitCode = 2` on the
ECS/console entry point) so Step Functions can wait and re-invoke it — but AWS's `ecs:runTask.sync`
integration treats *any* non-zero essential-container exit code as a task failure
(`States.TaskFailed`), indistinguishable from a genuine failure without parsing the exit code back
out of the opaque `Cause` string. On Lambda, the same distinction is just a JSON field
(`LambdaEntry` returns the step's result object directly), which a `Choice` state can branch on
cleanly. So a deployment that wants the wait/re-poll loop needs `graph-load-poll` on Lambda, or a
different in-progress signal on ECS (e.g. status written to S3/DynamoDB and read back via a
separate SDK-integration state) instead of exit-code branching. Every other step's choice of
ECS vs. Lambda is unconstrained.

- `Program.cs` registers four named steps and hands them to whichever entry point matches how
  it's being invoked, branching on `AWS_LAMBDA_RUNTIME_API` (set by the Lambda execution
  environment, never set under ECS/Batch):
  - **ECS/console** (default): `Thor.Workflows.Abstractions.WorkflowHost.RunAsync`. Each
    invocation reads two env vars: `THOR_STEP` (which step to run) and `THOR_INPUT` (JSON
    payload, deserializes to `IngestionRequest`). The step's result maps to a process exit code
    (0 = completed, `WorkflowHost.RetryLaterExitCode` = still in progress, 1 = failed); Step
    Functions drives retries/DLQ off that exit code — the step never talks to Step Functions
    directly.
  - **Lambda**: `Thor.Workflows.Abstractions.LambdaEntry.RunAsync`. Which step a given Lambda
    function runs is still selected by `THOR_STEP` (one Lambda function per step, sharing the
    same deployed artifact), but the input comes from the raw Lambda invocation event instead of
    `THOR_INPUT`, and the step's own result object is returned directly as the Lambda's JSON
    response — so Step Functions reads a field like `Outcome` straight off the output instead of
    an exit code.

```csharp
public sealed record IngestionRequest(
    Guid TenantId, string ExportLocation, Guid ScanId, Guid ScanManifestId,
    string? FileLocation = null, int BatchSeq = 0, Guid? RunId = null);
```

`TenantId` routes every step to the right tenant database via
`TenantConnectionManagerFactory.Build()` (the Master-DB tenant-routing chain, ADR §6.2/§6.3).
`ExportLocation` is just the bucket (e.g. `s3://thor-uploads-dev`, no prefix/path) — per-file paths
come from `ScanManifest.FileLocations` (§4). `ExtractAndStageStep` resolves `Source.ConnectorType`
**per file**, parsed from that file's own `FileLocation` via `Thor.S3.UploadKeyParser`, and passes
it to `ConnectorNormalizerFactory` (§5) — a manifest can span more than one connector (a scan can
run several sources at once) even though it's always scoped to exactly one tenant and one scan.
That's also why `IngestionRequest` carries no `SourceId` of its own.

`FileLocation`/`BatchSeq` are used only by `extract-stage`, which is deliberately **one deployed
step (one Lambda function, or one ECS task definition) invoked in two shapes** rather than a
separate deployment per shape (no extra function/task-def to deploy or wire up):
- `FileLocation` left `null` — list this manifest's files: reads `ScanManifest.FileLocations` and
  emits one `IngestionRequest` per file (each with `FileLocation`/`BatchSeq` set). This is what a
  Step Functions Distributed Map's `ItemsPath` fans out over.
- `FileLocation` set — a Map item: extract, normalize, and stage exactly that one file.

Every other step ignores both fields entirely.

| `THOR_STEP` | Class | Purpose |
|---|---|---|
| `extract-stage` | `ExtractAndStageStep` | List a manifest's files (no `FileLocation`), **or** resolve connector → normalize → stage one file (`FileLocation` set) |
| `promote` | `PromoteStep` | Mark the manifest `extract_and_stage_complete`, then CDC diff + upsert staging → canonical, all four entity kinds |
| `graph-load-start` | `GraphLoadStartStep` | Resolve edges, kick off Neptune bulk load |
| `graph-load-poll` | `GraphLoadPollStep` | Poll the Neptune bulk-load job to completion |

## 3. End-to-end flow

```
S3 (connector export .zip / JSON)
   │
   ▼
extract-stage (list mode) ── read ScanManifest.FileLocations, emit one IngestionRequest per file
   │
   ▼
extract-stage (Step Functions Distributed Map) ── every file in the manifest, concurrently,
   │              same deployed step as the list step above, invoked once per file
   │              resolve Source.ConnectorType → normalizer (Ad/CyberArk/Windows) per file
   │              staging_account / staging_grp / staging_asset / staging_entitlement
   ▼
promote ── marks the manifest's workflow row extract_and_stage_complete (the Map's own
   │        completion, guaranteed by Step Functions before this state runs, is the actual
   │        "combine" point — this is just the status write), then hash-diff upsert staging →
   │        canonical, emit ingest_change_event, delete staging rows, maintain entity_alias
   ▼
graph-load-start ── resolve deferred edge refs → tenant.edge
   │                 partition changed accounts/groups/edges into delete vs. load
   │                 write Gremlin bulk-load CSVs to S3, start Neptune bulk load job
   ▼
graph-load-poll ── poll Neptune until the load job reaches a terminal state
```

Each arrow is a separate Step Functions state, except `extract-stage`, which is two states against
the *same deployed step* (a plain `Task` invocation to list files, then a Distributed Map of
concurrent per-file invocations), and `graph-load-start`, which resolves edges and starts the
graph load in one invocation. The Map's per-item results are **not** merged into the Map state's
own output and never flow into `promote`'s input at all — `PromoteStep` only ever needs
`TenantId`/`ScanId`/`ScanManifestId`/`RunId`, all of which are already known from the list step,
before the Map runs. Concretely: the Map's `ResultPath` is `null` (its output is discarded), its
`ResultWriter` writes each item's result to S3 instead of inlining it into execution state, and
`promote`'s input is built by a `Pass` state reading `TenantId`/`ExportLocation`/`ScanId`/
`ScanManifestId`/`RunId` straight from the *list* step's output. This removes the Step Functions
state-size limit (~256KB) as a concern structurally — a manifest's per-file result volume can
never affect execution state size, however many files it has. See §3.1 for where the `ResultWriter`
output and the DLQ queue actually live.

**Partial-file-failure policy**: files within a manifest are independent, so `extract-stage`'s Map
tolerates a file failing. The per-file Task inside the Map's `ItemProcessor` carries an ASL `Retry`
(for transient errors — throttling/service errors from whichever compute target it runs on) and,
once retries are exhausted, a
`Catch` that sends the failed file's identity (`TenantId`/`ScanId`/`ScanManifestId`/`FileLocation`/
`BatchSeq`/`RunId`) and error to the **same DLQ queue** every other step's `Catch` already sends to
(reusing the queue itself — not the same ASL state, since `ItemProcessor` is its own isolated
sub-state-machine and can't reference a state in the parent workflow), then reshapes the outcome
into a non-throwing `{Success: false, FileLocation, Error}` `Pass` state rather than a `Fail`. Because
every iteration of `ItemProcessor` ends in a success-shaped state either way, the Map never sees a
failed item to tolerate — no `ToleratedFailurePercentage`/`ToleratedFailureCount` is needed. `promote`
then runs regardless, against whatever ended up staged. Redriving a DLQ'd file (or an ASL retry) is
safe without producing a duplicate canonical row: `Promoter` dedupes staged rows by
`(source_id, native_id)` ordered by `received_at DESC` and deletes all of the manifest's staging
rows after promotion (§7), a guarantee `RunAsync_RetriedForSameFile_
PromoteStillProducesExactlyOneCanonicalRowPerNativeId` exercises directly. `ExtractAndStageStep`'s
per-file path does no workflow-row bookkeeping on failure (unlike every other step, including its
own list-mode path) — see §10.1.

### 3.1 Result delivery & DLQ resourcing

The Distributed Map's `ResultWriter` (S3) and the DLQ queue the per-file `Catch` reuses are, like
the rest of the state machine, provisioned and managed **outside this repo** — there is no
Terraform/CDK/ASL for this state machine checked into `thor_backend` (`infra/` is currently just a
placeholder README); the live definition is maintained directly wherever it's deployed. This
section records the intended shape only:
- **`ResultWriter` bucket/prefix**: one S3 location per manifest run, e.g.
  `s3://<bucket>/ingestion-map-results/{ScanManifestId}/{ExecutionName}/` — exact bucket name is
  whatever the infra owner assigns; nothing in this repo reads these objects back.
- **DLQ queue**: the same SQS queue every other step's top-level `Catch` already sends to. A
  per-file DLQ message's body carries `TenantId`+`ScanId`+`FileLocation` — the idempotency-key
  fields ADR §9/§12 call for — plus `ScanManifestId`/`BatchSeq`/`RunId`/`Error`/`Cause`, so a
  redrive doesn't depend on parsing any exception message text.
- **`MaxConcurrency`** on the Map should be tuned against per-tenant RDS Proxy connection limits
  (ADR §9's noisy-neighbor concern) — not asserted here, since it's an infra-side tuning value.

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

`ExtractAndStageStep`'s list-mode path (`FileLocation` left `null`, see §2) looks up the
`ScanManifest` by `ScanManifestId`, reads its `FileLocations` (each entry a file's full S3 object
key, written by whatever creates the manifest), and emits one `IngestionRequest` per entry
(`BatchSeq` = the entry's fixed index) — this is the only path that reads the manifest row itself;
the per-file path never loads it.

`IExportSource`/`S3ExportSource` abstract where a connector's export comes from (`s3://` URIs) —
there's no discovery/listing step at the file-read level either. The per-file path receives
exactly one file's `IngestionRequest`; `ExportLocation` supplies only the bucket (e.g.
`s3://thor-uploads-dev`, no prefix/path), and the identifier is built as
`s3://{bucket}/{fileLocation}` and read directly — a `FileLocation` that doesn't actually exist in
S3 surfaces as a natural `GetObject` failure that propagates, rather than a silent shortfall.

The file's `FileLocation` is `Thor.S3.UploadKeyParser.TryParse`'d for its own
`TenantId`/`SourceId`/`ScanId` (`tenants/{tenantId}/uploads/{sourceId}/{scanId}/{fileName}` — the
same key shape in `UploadService.CreatePresignedUploadUrlAsync`; a parsed `TenantId` that doesn't
match the request's own `TenantId`, or a parsed `ScanId` that doesn't match the request's own
`ScanId`, throws (fail-closed; both checks are data-integrity checks, not security boundaries,
since the tenant DB connection is already established/trusted by that point). The parsed
`SourceId` resolves `Source.ConnectorType` and thus which normalizer runs — **per file**, not once
for the whole run, since a manifest can span more than one connector even though it's always
scoped to one tenant and one scan. `ZipExportReader` unzips one archive and returns its first real
entry, skipping zip directory placeholders.

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

`GraphLoadPollStep` checks the load job's status **once** per invocation — it does not wait or
loop in-process. Completion (no row errors) marks the job/workflow `"completed"`. Completion
with row errors or outright failure marks the job `"failed"` and throws, mapping to a non-zero
exit code (ECS) or a Lambda invocation error, for Step Functions to catch/escalate. Any other
status leaves the job/workflow in their in-flight (`"started"`) state and reports "in progress"
via the step's own result (`GraphLoadPollResult.Outcome`/`IsInProgress`) rather than throwing —
Step Functions is expected to wait and re-invoke this step later rather than the step blocking
on its own. On the ECS/console entry point that "in progress" signal shows up as a distinct
process exit code (`WorkflowHost.RetryLaterExitCode`, since ECS/Batch expose only the exit code
to Step Functions); on the Lambda entry point it's just a field in the JSON response, since
Lambda's return value is visible to Step Functions directly.

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

### 10.1 Workflow lifecycle tracking

Every step, and `extract-stage`'s list-mode path, calls `Thor.Workflows.Abstractions.
WorkflowLifecycle` (shared across workflow modules, not ingestion-specific) to create/update its
own row in the tenant `workflow` table: `EnsureStartedAsync` on entry (get-or-create, idempotent —
a retried call doesn't duplicate the row; called by `extract-stage`'s list mode, `promote`, and
`graph-load-start`), `MarkCompletedAsync` on `graph-load-poll`'s success, `MarkFailedAsync` on any
exception. `WorkflowType = "ingestion"`, `Trigger = "step_functions"`.

Between "started" and the pipeline's final `MarkCompletedAsync`/terminal failure, each step also
records its own progress via `WorkflowLifecycle.MarkStatusAsync` (status-only — it doesn't touch
`StartedAt`/`CompletedAt`/`Error`), using the constants in `Constants/IngestionStatuses.cs`:

| Status | Set by |
|---|---|
| `started` | Whichever of `extract-stage` (list mode)/`promote`/`graph-load-start`/`graph-load-poll` runs `EnsureStartedAsync` first |
| `extract_and_stage_complete` | `promote`, as the very first thing it does |
| `promote_complete` | `promote`, after all four entity kinds have been promoted |
| `graph_load_started` | `graph-load-start`, at each of its successful return points (a load actually started, nothing needed loading, or another invocation already has one pending) |
| `completed` | `graph-load-poll`, once the Neptune bulk load is confirmed done (or there was no pending job to poll) |
| `extract_and_stage_failed` | An unhandled exception in `extract-stage`'s list mode |
| `promote_failed` | An unhandled exception in `promote`, including its own `extract_and_stage_complete` status write |
| `graph_load_failed` | An unhandled exception in `graph-load-start` or `graph-load-poll` |

Every step's catch block calls `MarkFailedAsync` with its own step-specific status (via that
method's optional `status` parameter, which defaults to the generic `"failed"` that
`WorkflowLifecycleTests` exercises directly) — so a failed run's `workflow.status` names which
phase failed, not just that something did.

`extract_and_stage_complete` is set by `promote` rather than a dedicated step: the Distributed
Map's per-file results are merged purely at the ASL level (§3) — `promote` is simply the first
point in application code that runs after the Map, since Step Functions guarantees every file item
finished before advancing to it. Giving this one status write its own Lambda function was
considered and rejected — it would be a 5th deployed function for a single line of work with no
data dependency, so it's folded into the start of `promote`'s existing `try` block instead (any
failure writing it is indistinguishable from any other `promote` failure — both land on
`promote_failed`).

`ExtractAndStageStep`'s **per-file** path (`FileLocation` set) deliberately does **not** touch the
workflow row on failure, unlike its own list-mode path or any other step. It runs once per file,
concurrently with every other file in the same manifest, against one shared manifest-level
workflow row — if it called `MarkFailedAsync` the way every other step does, one bad file would
flip the whole manifest's status to `"failed"` even though the pipeline's policy is to tolerate
that file's failure and continue (§3), and concurrent Map iterations would race writing to the
same row. Its exceptions propagate uncaught instead, for the Step Functions ASL layer to catch and
reshape into a tolerated per-item result (see §3.1 for the concrete Retry/Catch/DLQ mechanics).

**`RunId` (`Guid?`) — distinguishing a step-level retry from a fresh re-trigger, and the key for
standalone/API-triggered workflows.** The row is looked up by `RunId` when one is given, otherwise
by `(ScanManifestId, WorkflowType)` (the old, still-default behavior for any caller not yet passing
one). `RunId` is a caller-supplied identifier for **one invocation attempt** — it must stay the
same across that attempt's own internal retries and be **freshly minted** for a genuinely new
attempt (e.g. whatever re-drives a DLQ'd message into a new execution). That gives:
- A step retried within the same execution → same `RunId` → same row found → reused/overwritten.
- A whole pipeline re-triggered after DLQ exhaustion → new `RunId` → no existing row matches → a
  **new** row is created, and the old failed row is left untouched as history
- A standalone/API-triggered workflow (`ScanId`/`ScanManifestId` both `null`, `Trigger = "api"`) is
  tracked by `RunId` alone — there's nothing else to key it on, so `EnsureStartedAsync` throws if
  both `ScanManifestId` and `RunId` are missing.


The standalone/API-trigger path itself is implemented and tested at the `WorkflowLifecycle` level
(nullable `ScanId`/`ScanManifestId`, `RunId`-only lookup), even though no caller exists in this repo
yet (Since ingestion is always scan-driven) — whoever builds that caller must
mint/thread `RunId` with the same discipline described above, or that path gets neither idempotency
nor retry/retrigger separation.

## 11. Constants

- **`ConnectorTypes`**: `ActiveDirectory = 308`, `CyberArk = 401`, `Windows = 402`.
- **`RelTypes.TwoSided`**: `{ "MEMBER_OF" }` — the one relationship type stored reciprocally on
  both endpoints. Every other rel type (`REPORTS_TO`, `MANAGED_BY`, `STORED_IN`, `HAS_ACCESS`)
  is one-sided and depends on healing (§8.1) to resolve if its target arrives later.

## 12. Where things live

```
backend/workflows/Thor.Workflows.Ingestion/
├── Program.cs                        entry point — registers the 4 steps
├── IngestionRequest.cs                shared input contract — extract-stage's two shapes (list vs. per-file) plus promote/graph-load-*
├── Steps/                             one class per THOR_STEP value
│   ├── ExtractAndStageStep.cs         list mode + per-file mode, same deployed step either way
│   ├── PromoteStep.cs                 marks extract_and_stage_complete first, then promotes
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
├── Constants/                         RelTypes, ConnectorTypes, IngestionStatuses
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
