# Ownership Workflow

> Covers `backend/workflows/ownership/Thor.Workflows.Ownership/` end to end —
> deployment shape, the data-driven rule engine, the three-phase wave loop,
> the walk propagation, persistence/idempotency, and the Neptune graph sync —
> plus the data model in `backend/shared/Thor.DataLayer` it reads and writes.
> One of the ADR §16 workflow modules: independent, additive, tenant-scoped,
> idempotent. Built on the same platform as ATRE (`atre_workflow.md`); where
> the two work the same way this document says so and points there rather
> than repeating it.

## 1. What Ownership does

Ownership proposes an owning `IdentityRecord` (an HR/directory person) for
`account`, `grp` (group), and `asset` entities. Unlike ATRE's classification
rules (single-entity attribute predicates), Ownership's rules are
fundamentally *correlation* rules — they either match the entity against a
candidate identity, match it against another entity, or infer an owner from
something already-resolved nearby in the graph.

Every rule is a plain `ownership_rule` row: **nothing about what a rule
matches, which fields, which edges, or its confidence weight is hardcoded in
C#.** Changing a rule — retuning its weight, turning it on/off, or defining an
entirely new correlation using the fields/edges the engine already knows
about — is a DB `UPDATE`/`INSERT`, never a redeploy. Only a genuinely new
*kind* of rule (see §3) needs a code change.

This is a pure background proposal engine — there is no confirm/review UI,
and no API for a human to accept/override a proposal. `PartyAssignment` rows
are written directly.

**Postgres is the system of record; Neptune is a projection.** Every rule —
including the graph-shaped ones — runs as set-based SQL over `tenant.edge`,
the canonical edge set `EdgeResolver` promotes during ingestion. Neptune
receives the run's winners afterwards as `OWNED_BY` edges (§7), for traversal
queries to read. Nothing in the run reads Neptune back.

## 2. Deployment shape & entry points

Nine workflow steps (`OwnershipSteps`), built into the one image
`backend/workflows/Dockerfile` produces for every workflow module
(`--build-arg WORKFLOW=Ownership`), all deployed on Lambda
(`infra/src/workflow_definitions.tf`):

| `THOR_STEP` | Step | Runs |
|---|---|---|
| `start-run` | `OwnershipStartRunStep` | once, before anything else |
| `walk` | `OwnershipWalkStep` | once per phase, before its first wave — looped while it reports `IsInProgress` (§5) |
| `vote` | `OwnershipVoteStep` | the Distributed Map body — once per chunk |
| `next-wave` | `OwnershipNextWaveStep` | once per wave |
| `graph-load-start` | `OwnershipGraphLoadStartStep` | once, after the last phase (role `graph-load`) |
| `graph-load-poll` | `OwnershipGraphLoadPollStep` | looped every 30 s until the load is terminal (role `graph-load`) |
| `finalize` | `OwnershipFinalizeStep` | once, at the end |
| `record-failure` | `OwnershipRecordFailureStep` | on the run's Catch path, when the run itself dies |
| `record-chunk-failure` | `OwnershipRecordChunkFailureStep` | on a Map item's Catch path, once per lost chunk |

`Program.cs` registers them and hands them to
`Thor.Workflows.Hosting.WorkflowEntryPoint.RunAsync`; each derives from
`WorkflowStep<T>`. Tenant routing comes from the shared
`Thor.Workflows.Hosting.Composition.TenantConnectionManagerFactory`.

### 2.1 The request, and the two ways in

`OwnershipRequest(Guid TenantId, Guid? ScanManifestId = null, Guid? RunId = null, string? Scope = null)`
is the one payload shape every run-level step reads.

| Started by | `ScanManifestId` | `RunId` | Default `Scope` | Trigger recorded |
|---|---|---|---|---|
| Ingestion (`StartOwnership`) | set | absent — derived (§6) | `unassigned_and_confirmed` | `step_functions` |
| A caller, tenant-wide | absent | **required** | `unassigned` | `api` |

`OwnershipScopes.Validate` runs in `start-run` before the workflow row is
opened: an unknown scope, or a tenant-wide request with no `RunId`, fails the
run with nothing to close. A `RunId` is required there because without a
manifest there is nothing to derive one from, and every step of the run must
arrive at the same id to find the same rows. The two defaults preserve what
the branch's two separate steps (`vote` and `api-vote`) each did; §4.4
explains what they mean.

There is no Thor.Api endpoint that starts a tenant-wide run yet; today it is
started directly against the state machine with an input such as
`{"TenantId": "…", "RunId": "…", "Scope": "all"}`.

### 2.2 How ingestion starts it

`ingestion.asl.json`'s `StartAtre` now continues to `StartOwnership`, which
starts this state machine fire-and-forget, the same way and for the same
reasons as `StartAtre` (see `atre_workflow.md` §2.1): the target ARN is built
from the naming rule and declared in ingestion's `chained_workflows`; `Input`
is projected field by field so ingestion's `RunId` never crosses; and no
execution `Name` is pinned, which is safe because every write is keyed on the
deterministic run id.

Ownership is chained from ingestion rather than from ATRE because it reads no
ATRE output. What it needs from ingestion — the entities, the resolved edges,
and (for the graph sync) their Neptune vertices — is all in place by the time
ingestion's own graph load has finished, which is where `StartOwnership` sits.

### 2.3 The loop

`infra/src/modules/workflow/asl/ownership.asl.json`:

```
Envelope → StartRun → Walk ⟲ WalkCheck → VoteWave (Distributed Map) → NextWave → MoreWaves
                        ↑                  ↑                                        │
                        │                  └────────────── HasMore, same phase ─────┤
                        └──────────────────────────────── HasMore, PhaseStart ─────┤
                                                                                    │ HasMore = false
                            GraphLoadStart → GraphLoadPoll ⟲ (Wait 30 s) → Finalize → WorkflowSucceeded

any Task's Catch ──► RecordFailure ──► WorkflowFailed
inside the Map:  VoteChunk ──Catch──► RecordChunkFailure ──► VoteChunkTolerated
```

The wave mechanics are ATRE's (`atre_workflow.md` §2.2): a chunk is a window
— an offset and a limit — that the `vote` invocation reads for itself; a wave
is `chunks_per_wave` chunks; nothing counts the scope up front, and a wave
that comes back short ends the loop; the Map items are built whole in C# with
no `ItemSelector`; a failed chunk is recorded and tolerated, counts as a full
one, and a wave where every chunk failed stops. Ownership has no dead-letter
queue, for ATRE's reasons.

What Ownership adds is the phase. `OwnershipWave` carries `EntityType` and
`PhaseStart`, and `OwnershipWavePlanner.NextWaveAfter` moves to wave zero of
the next phase — `account` → `grp` → `asset` — when a wave comes back short,
setting `PhaseStart` so `MoreWaves` routes through `Walk` first. After the
last phase it answers `HasMore = false`. A wave where every chunk failed ends
*that phase* rather than the run: a phase that cannot be read says nothing
about the others, and its lost windows are already in `workflow.error`.

`OwnershipChunkSummary.EntitiesScanned` — what the planner sums — is the
number of in-scope entities in the window, matched or not, counted by
`OwnershipCandidateSqlBuilder.BuildWindowCount` over the same `scoped` CTE
the candidate query uses. It cannot be derived from the candidates: an entity
no rule matches produces no candidate row.

| Dial | Default | Set from |
|---|---|---|
| `THOR_OWNERSHIP_ENTITIES_PER_CHUNK` | 10,000 | `var.workflows["ownership"].settings.entities_per_chunk` |
| `THOR_OWNERSHIP_CHUNKS_PER_WAVE` | 5 | `…settings.chunks_per_wave` — keep equal to `map_max_concurrency` |
| `THOR_OWNERSHIP_WALK_BUDGET_SECONDS` | 600 | `…settings.walk_budget_seconds` |
| `map_max_concurrency` | 5 | `…settings.map_max_concurrency` — a connection dial: 2 per body |

None of these defaults has been measured against a real tenant (§10).

## 3. The rule engine (`Rules/`)

### 3.1 Schema

`OwnershipRule` (`backend/shared/Thor.DataLayer/Models/Tenants/OwnershipRule.cs`)
carries three columns that fully describe a rule:

| Column | Purpose |
|---|---|
| `rule_type` | Which of the four generic handlers below applies this row |
| `applies_to` | Which entity type it targets — `account` / `grp` / `asset` |
| `rule_definition` | A flat JSON object of parameters for that `rule_type` |

`precision_score` is the vote's weight; `is_active` gates whether the rule
runs at all — an inactive rule contributes nothing, not a fallback weight.
`total_predictions` / `true_positive_count` / `false_positive_count` exist
for future precision calibration but aren't written by any code today.

**No rule-type-specific logic ever references a rule by name.** A handler
only knows how to interpret its `rule_type`'s generic parameter shape — which
`relType`, which fields, which weight, which entity type are 100% row
content, read at runtime.

### 3.2 The four `rule_type` values

**`field_match`** — correlate the entity (optionally after a hop or chain of
hops) against a candidate identity.

```json
// Email exact match — no hop needed
{ "field": "email", "how": "exact" }
```
```json
// UPN vs email local-part — fields differ, so name both sides
{ "entityField": "upn_local_part", "identityField": "email_local_part", "how": "exact" }
```
```json
// Manager attribute — hop to the manager's own account row, then compare its email
{ "via": { "relType": "REPORTS_TO", "direction": "out", "toType": "account" },
  "field": "email", "how": "exact" }
```
```json
// Department fallback, capped at 5 candidates per account
{ "field": "department", "how": "exact", "cap": 5 }
```
```json
// Employee ID substring in the account name
{ "entityField": "sam_account_name", "identityField": "employee_id", "how": "contains" }
```
```json
// CyberArk account-via-safe — a two-hop chain, then correlate
{ "via": [
    { "relType": "STORED_IN", "direction": "out", "toType": "asset" },
    { "relType": "HAS_ACCESS", "direction": "in", "fromTypes": ["account", "grp"] }
  ],
  "field": "email", "how": "exact" }
```

`how` ∈ `exact | similar | contains | starts_with | ends_with` (§3.4). `via`
is a single hop object **or** an array (a chain) — a chain steps from one
hop's target to the next hop's source. `cap` limits how many candidates a
rule may contribute per account (see the department example above). An
optional `"and": [...]` array of the same
`{field|entityField/identityField, how}` shape ANDs in extra conditions
(rare — e.g. a two-field match). Every match is its own ranked candidate.

**`sibling_match`** — correlate against *another row of the same entity
type* (no `via` — always a same-type self-join), inheriting that sibling's
own current owner. Since more than one sibling can match with different
owners, resolution always aggregates by majority (§3.5); there's no separate
flag for this.

```json
{ "field": "display_name", "how": "exact" }
```
```json
{ "field": "display_name", "how": "similar" }
```

**`inherit_owner`** — take one related entity's already-active owner. `via`
(a hop or chain) picks exactly one target per source row (via `LATERAL ...
LIMIT 1`, optionally preferring an edge by a boolean prop); `walk` repeats a
hop — an edge or a self-referencing FK column — until the current row has an
owner, bounded by `maxDepth`.

```json
// Group MANAGED_BY inheritance — one hop
{ "via": { "relType": "MANAGED_BY", "direction": "out", "toType": "account" } }
```
```json
// Group nesting — walk parent groups until one is owned
{ "walk": { "relType": "MEMBER_OF", "direction": "out", "toType": "grp" }, "maxDepth": 10 }
```
```json
// Asset parent chain — walk a self-referencing FK until an ancestor is owned
{ "walk": { "column": "parent_asset_id" }, "maxDepth": 20 }
```
```json
// CyberArk privileged-access inference, preferring an admin-access principal
{ "via": { "relType": "HAS_ACCESS", "direction": "in", "fromTypes": ["account", "grp"] },
  "prefer": { "prop": "is_admin", "value": true } }
```

**`majority_owner`** — fan out along a hop chain to many related entities,
resolve each one's own owner, and propose whichever owner recurs most often
(§3.5).

```json
// Majority manager (group) — member accounts' managers, most common wins
{ "via": [
    { "relType": "MEMBER_OF", "direction": "in", "fromType": "account" },
    { "relType": "REPORTS_TO", "direction": "out", "toType": "account" }
  ] }
```
```json
// Majority group owner (account) — most common owner across this account's groups
{ "via": { "relType": "MEMBER_OF", "direction": "out", "toType": "grp" } }
```

A `schemaVersion` field (currently always `1`) is embedded in every
`rule_definition`; the validator rejects a version it doesn't understand — a
cheap forward-compat hook with no DDL cost.

### 3.3 Validation & injection safety (`backend/shared/Thor.Rules/Ownership/RuleDefinitionValidator.cs`)

Runs once per rule inside `OwnershipRuleLoader.LoadActiveAsync`, after JSON
deserialization but before the rule is trusted for SQL generation. An invalid
rule is skipped and logged, never fatal to the run.

- Every field name resolves through `EntityFieldCatalog`/`IdentityFieldCatalog`
  — a fixed, hand-written allowlist mapping a logical name to a safe SQL
  expression. An unrecognized field fails validation before any SQL is built.
- Every hop resolves through `EdgeTraversalCatalog`'s allowlist of
  `(relType, direction, fromType, toType)` tuples — `fromType`/`toType` are
  always the edge's own literal columns, regardless of which side a rule
  treats as "current" vs. "target."
- A hop chain that fans into more than one entity type (e.g. `HAS_ACCESS`
  from either an account or a group) is only allowed as the **last** hop —
  nothing downstream needs to hop further from a polymorphic row set, and
  disallowing it keeps the compiler simple.
- `field_match`'s top-level condition must have a resolvable `field`/`how`;
  `sibling_match` rejects `via` outright; `inherit_owner` requires exactly
  one of `via`/`walk`; `majority_owner` requires `via`. `walk`'s FK-column
  mode is checked against a fixed per-entity-type allowlist
  (`SelfReferencingForeignKeys`, currently just `asset.parent_asset_id`).

**No comparison ever involves a rule-supplied literal value** — every leaf
compares two column expressions — so no comparison needs its own bound
parameter. Every hop's `rel_type`/entity-type string and every numeric cap
*is* still bound as an `NpgsqlParameter`, never concatenated, as defense in
depth beyond the allowlist checks above. The one place raw JSON text is
interpolated directly into SQL is `walk`'s FK column name — safe only because
the validator already checked it against the fixed allowlist before the
compiler ever sees it.

**No phase-order restriction.** A rule may reference a hop target type from a
later pipeline phase (§4) than its own `applies_to` — e.g. a `majority_owner`
rule on `account` reading `grp` assignments. `party_assignment` is a durable,
cross-run table, so such a rule simply finds nothing on a tenant's very first
run and converges over subsequent runs — the same self-healing behavior
`inherit_owner`'s multi-level hierarchy walk already relies on.

### 3.4 Comparison operators (`Rules/RuleOperator.cs`, `Rules/OwnershipRuleCompiler.cs`)

| `how` | Semantics |
|---|---|
| `exact` | Case-insensitive equality (both sides lowercased by the field catalogs), with null/empty guards on both sides |
| `contains` / `starts_with` / `ends_with` | `position()`/`left()`/`right()`-based substring checks — **not** `LIKE`, specifically to avoid the compared value's own characters being misinterpreted as wildcards |
| `similar` | Delimiter-bounded prefix/suffix match — see below |

**`similar`**: after lowercasing, two values are similar if one is a
delimiter-bounded prefix or suffix of the other. `jsmith_admin` is similar to
`jsmith` (a delimiter-separated variant of the same base name); `jsmithadmin`
is **not** (no separator). Concretely, `L`/`R` are similar if any of:

1. `L` starts with `R` immediately followed by a delimiter/digit character
2. `R` starts with `L` immediately followed by a delimiter/digit character
3. `L` ends with a delimiter/digit character then `R`
4. `R` ends with a delimiter/digit character then `L`

using the delimiter/digit set `- _ $ % # & ! ^ ( ) { } 0-9 .`. Compiled to
Postgres regex (`~`) matching, with the *compared value* escaped via
`regexp_replace(value, '([.^$*+?()\[\]{}|\\])', '\\\1', 'g')` before being
concatenated into the pattern — it's row data being treated as a literal
substring, never rule-author-supplied regex syntax.

### 3.5 SQL compilation (`Rules/OwnershipRuleCompiler.cs`)

Each rule compiles to a `CompiledRuleFragment` — zero or more auxiliary CTEs
plus a `SELECT entity_id, identity_id FROM ...` body — assembled by
`Rules/OwnershipCandidateSqlBuilder.cs` into one query per chunk (§4).

- **Hop chains preserving fan-out** (`BuildEdgeChain`, used by `field_match`'s
  `via` and `majority_owner`): plain `JOIN`s across `tenant.edge`, so every
  reachable target becomes its own row — either an independent candidate
  (`field_match`) or a vote to be tallied (`majority_owner`).
- **Hop chains picking one target** (`CompileInheritOwnerVia`, used by
  `inherit_owner`'s `via`): each hop is a `CROSS JOIN LATERAL (... ORDER BY
  ... LIMIT 1)`, since `inherit_owner` always resolves to a single candidate
  per entity — `prefer` (only meaningful on the last hop) breaks ties among
  multiple reachable targets.
- **Polymorphic field resolution**: if a `field_match` chain's last hop fans
  into more than one type, the final field is resolved via `COALESCE` of
  per-type correlated subqueries (one per declared type, each guarded by
  `WHERE ... AND <type column> = 'that type'`) rather than joining every
  declared type's table, which would multiply rows.
- **Walks** (`CompileInheritOwnerWalk`): not compiled into the phase query
  at all any more. The walk step has already staged every entity's nearest
  owned ancestor in `ownership_walk_candidate` (§5), and the rule compiles to a
  join from the window to those rows.
- **Owners from this phase and run are invisible** (`PriorOwnersOnly`): every
  join to `party_assignment` ignores rows this run wrote for the phase being
  matched — see §4.3.
- **Majority aggregation** (`BuildMajorityAggregation`, shared by
  `sibling_match` and `majority_owner`): `GROUP BY entity_id, identity_id,
  COUNT(*)`, then `DISTINCT ON (entity_id) ... ORDER BY votes DESC,
  identity_id` — highest count wins, deterministic tie-break by identity id.

### 3.6 Field catalogs (`backend/shared/Thor.Rules/Ownership/EntityFieldCatalog.cs`, `IdentityFieldCatalog.cs`)

| Entity type | Logical fields |
|---|---|
| `account` | `email`, `upn_local_part`, `display_name`, `sam_account_name`, `domain_name`, `department` (→ `raw_attributes::jsonb ->> 'Department'` — capital D, matching `Stager.cs`'s default `JsonSerializer` PascalCase output) |
| `grp` | `email`, `display_name` |
| `asset` | `display_name` |
| `identity` (either side of any rule) | `email`, `email_local_part`, `display_name`, `department`, `employee_id` (→ `hr_employee_id`) |

`IdentityRecord` has no network-login-ID-equivalent field (e.g. a
SamAccountName/domain login), so a rule correlating on that shape isn't
representable today without approximating it via `email_local_part`, which
isn't a faithful match. A rule needing AD's `description`/`info` free-text
attributes isn't representable either until those attributes are captured by
an ingestion normalizer — see §10 for both gaps.

## 4. Matching a chunk

### 4.1 Seeding

`start-run` calls `OwnershipRuleSeeder.EnsureSeededAsync`, which idempotently
inserts the built-in rule catalog (`backend/shared/Thor.Rules/Ownership/OwnershipRuleDefaults.cs`) via one
raw `INSERT ... ON CONFLICT (rule_name) DO NOTHING`. **Insert-only, never
`DO UPDATE`** — an operator can edit `rule_definition`/`precision_score`/
`is_active` directly with no redeploy, so seeding must never clobber an
existing row (cost: a fix to a *built-in* rule's own logic needs a manual
per-tenant `UPDATE` to reach tenants that already seeded it). It runs once per
run, not once per chunk.

### 4.2 One chunk

`OwnershipVoteStep` handles one window of one phase:

1. Load the phase's active rules (`OwnershipRuleLoader.LoadActiveAsync`). None
   means nothing can match: the chunk reports zero entities, which ends the
   phase after this wave.
2. Count the window (`OwnershipCandidateReader.CountWindowAsync`).
3. Stream candidates (`OwnershipCandidateReader.StreamAsync`) from the query
   `OwnershipCandidateSqlBuilder.Build` assembles: a `scoped` CTE (rows not
   deleted, optionally restricted to one manifest's `IngestChangeEvent` rows,
   filtered by the run's assignment scope, §4.4, then `ORDER BY id OFFSET
   LIMIT` for the window), one compiled CTE per active rule, a `UNION ALL`
   tagging each row with its rule's id and weight, and a final collapse —
   `DISTINCT ON (entity_id, identity_id) ORDER BY weight DESC, rule_id` —
   keeping only the highest-weight rule per (entity, identity) pair.
4. Stage and flush through `OwnershipResultWriter` (§6).

Reads use one `TenantDbContext` and writes a second, so committing writes
never disturbs the read side's streaming snapshot — the same two-connection
design as `AtreClassifyStep`.

### 4.3 What makes chunking correct

A phase used to be one query. It is now many chunks writing as they go, and
two things had to change for that to give the same answer:

- **Windows ignore this run's own assignments.** The assignment-scope filter
  excludes entities that already have a `party_assignment` — and the run
  writes those as it goes. A filter that saw them would shrink the scope under
  the later windows and shift every offset past the entities they were meant
  to cover. `BuildAssignmentFilter` therefore ignores rows with this run's
  `run_id`, so an offset names the same entities for the whole run. Retries
  stay idempotent through the writer's `ON CONFLICT DO NOTHING`.
- **Rules read only owners from before the phase began.** Every join a rule
  makes to `party_assignment` (`sibling_match`, `inherit_owner` via,
  `majority_owner`) carries `PriorOwnersOnly`: this run's rows for the phase
  being matched are invisible, every prior run's and this run's earlier phases'
  are not. The single phase query saw exactly that by construction; without
  the predicate, a chunk would see whatever earlier chunks of the same phase
  had just assigned, and which owner a sibling contributed would depend on
  which window ran first.

`OwnershipVoteStepTests` pins both, and fails with either predicate removed.

### 4.4 Assignment-status scope (`Constants/OwnershipAssignmentScope.cs`)

Independent of manifest scoping, the `scoped` CTE also filters by each
entity's current `party_assignment` state, expressed over
`PartyAssignment.IsActive`/`IsOverride`:

- **unassigned** — no active `party_assignment` row for the entity
- **proposed** — an active row with `IsOverride = false` (what this engine
  itself writes)
- **confirmed** — an active row with `IsOverride = true` (not written by any
  code today — reserved for a future human-confirm/override action)

| `OwnershipAssignmentScope` value | Excludes |
|---|---|
| `all` | nothing |
| `unassigned` | any entity with an active assignment — the tenant-wide default |
| `unassigned_and_proposed` | only confirmed entities |
| `unassigned_and_confirmed` | only proposed entities — the ingestion-driven default |

In every case rows written by the current run are ignored (§4.3).

## 5. Walk rules: frontier propagation (`Walk/OwnershipWalkPropagator.cs`)

`inherit_owner` rules with a `walk` — group nesting, asset parent — give an
entity the owner of its nearest owned ancestor. The branch computed that with
a `WITH RECURSIVE` walk up from every scoped entity to `maxDepth`. That
expanded **paths, not nodes**: with no visited-set, a group reachable two ways
was expanded twice, diamond-heavy nesting grew exponentially with depth, and
every walk ran to the full depth before discarding everything but the nearest
owner.

The walk step replaces it with breadth-first propagation **down** from the
entities that already have an owner, once per phase, before the phase votes:

- **Level 1** is every child of an owned entity, inheriting that owner.
  **Level k** is every child of a level k−1 row, inheriting what that row
  inherited. Each level is one `INSERT … SELECT … ON CONFLICT DO NOTHING` into
  `ownership_walk_candidate`, whose primary key `(run_id, rule_id, entity_id)`
  is the visited-set: an entity reached at an earlier level is never reached
  again. Each entity is expanded once per rule however many paths lead to it,
  cycles simply stop, and cost is linear in the edges reached. Propagation
  stops at `maxDepth` or at a level that stages nothing.
- **Direction.** Propagation follows the walk backwards: an `out` walk climbed
  child → parent along `from_id → to_id`, so a parent's children are the
  `from_id`s of edges arriving at it — which is what the `Edge(to_id,
  rel_type)` index serves. A `column` walk (`asset.parent_asset_id`) finds
  children by that column, whose FK index EF creates.
- **Same answer.** Starting from every owned entity at once and stopping at
  first arrival gives each entity the owner at the smallest depth — the
  recursive walk's nearest owned ancestor. An entity's own owner never counts
  for itself (it is a source, not a row), matching the recursive walk's
  `depth > 0`. Where two owners are equally near, the lowest identity id wins;
  keeping only that one per entity per level is exact, because the nearest
  owners of a child are the union of its parents'. The recursive walk picked
  among ties arbitrarily. `OwnershipWalkPropagatorTests` checks the two agree
  on a randomized DAG with diamonds and cycles, using the old recursive SQL as
  the oracle.
- **Sources are owners from before this run** (`run_id <> @runId`), so a walk
  never builds on this run's own same-phase results — deep nesting fills in
  over successive runs, as it always has — and a retried run seeds from
  exactly what the first attempt did.
- **Resumable.** The step starts levels until its time budget
  (`THOR_OWNERSHIP_WALK_BUDGET_SECONDS`) is spent, then answers `IsInProgress`;
  the state machine invokes it again and it resumes from the deepest staged
  level. At least one level runs per invocation, so the loop always advances.
  A level is one statement and is never cut short.
- **Read back as candidates.** The compiled walk rule is a join from the window
  to its staged rows, so chunks, ranking and persistence are unchanged.
  `finalize` deletes the run's staged rows; a run that fails keeps them, so a
  retry of the same run id resumes rather than redoes the walk.

The traversal lives in one class so a Neptune (or Neptune Analytics)
implementation could replace it without touching voting — should edges ever
stop being held in Postgres (§10).

## 6. Persistence & idempotency (`Persistence/OwnershipResultWriter.cs`)

Candidates for one entity are staged in memory and flushed in batches of
2000, plus once at the end of each chunk:

- `ownership_vote` rows are always inserted (idempotent via the unique
  `(entity_id, rule_id, run_id)` index). `entity_id` carries no foreign key:
  it holds account, group and asset ids alike.
- `party_assignment` rows upsert via `ON CONFLICT (entity_type, entity_id,
  identity_id, run_id) DO NOTHING`, returning only the rows actually newly
  inserted. The unique index that conflict target needs is declared on the
  model.
- **Only for those newly-inserted rows** does the writer add a
  `party_assignment_event` audit row (`event_type = "proposed"`, `actor =
  "ownership_workflow"`).

**Run identity** (`Persistence/OwnershipRunIdentity.cs`): Ownership's fixed
namespace GUID over the shared `Thor.Workflows.Hosting.DeterministicRunId`: an
explicit `RunId` wins; else a v5 GUID over `"{tenantId}:{manifestId}"`.

### 6.1 Workflow lifecycle tracking

As ATRE's (`atre_workflow.md` §6.1): `start-run` opens the row
(`EnsureStartedAsync` then `ReopenAsync`, so a retry does not inherit the last
attempt's errors) and links it under the ingestion row via `workflow_graph`;
`record-chunk-failure` appends each lost window to `workflow.error`;
`finalize` closes it `completed`, or `completed_with_errors` when the error
column is non-empty; `record-failure` closes it `failed` from the Catch path,
which is the only place that sees a Lambda killed by its timeout.

## 7. Neptune graph sync

After the last phase, `OwnershipGraphLoadStartStep` reads this run's rank-1
active `party_assignment` rows across all three entity types, writes an
`identity` vertex CSV and an `OWNED_BY` edge CSV — one edge per winning
entity — to **Ownership's own** graph-load bucket, and starts a Neptune bulk
load. `OwnershipGraphLoadPollStep` checks it once per invocation and the state
machine waits 30 s between checks, as ingestion's poll loop does; a failed
load, or one that completed with row errors, throws to `RecordFailure`.
Neither step touches the workflow row — `finalize` closes it.

- **Job tracking without a schema change.** The load is tracked in
  `GraphBulkLoadJob` with the run id in both `ScanId` and `ScanManifestId`.
  Neither column has a foreign key and a run id never equals a scan's id, so
  Ownership's jobs cannot collide with ingestion's under the unique
  `(ScanId, ScanManifestId)` index — and no column had to be added to a table
  existing tenants already have. The reservation follows ingestion's
  `GraphLoadStartStep`: a `starting` row is inserted before Neptune is called,
  so a retry after a crash cannot start a second load of the same data, and a
  reservation older than `THOR_GRAPH_BULKLOAD_START_STALE_SECONDS` is
  reclaimed.
- **Two buckets, one loader role.** Each workflow's graph-load role writes
  only its own bucket; Neptune's loader role reads the buckets of every
  enabled workflow in `neptune_bulk_load_workflows` (a list since this change;
  previously one name).
- The `account`/`grp`/`asset` vertices the edges point at are ingestion's,
  using the same vertex id scheme; ingestion's load has finished before it
  starts this run.

## 8. Data model (`backend/shared/Thor.DataLayer/Models/Tenants/`)

| Model | Purpose |
|---|---|
| `OwnershipRule` | A rule row: `rule_type`, `applies_to`, `rule_definition`, `precision_score` (weight), `is_active`. Unique on `rule_name`. Precision-tracking columns exist but nothing writes them. |
| `OwnershipVote` | One rule's vote for one entity in one run. Unique on `(entity_id, rule_id, run_id)`. No FK on `entity_id` (polymorphic). |
| `OwnershipWalkCandidate` | Per-run walk staging: `(run_id, rule_id, entity_id)` → `identity_id`, `depth`. Primary key is the traversal's visited-set; indexed on `(run_id, rule_id, depth)` for the frontier. Deleted at `finalize`. |
| `PartyAssignment` | The owner for an entity — polymorphic `entity_type`/`entity_id`. Unique on `(entity_type, entity_id, identity_id, run_id)`. |
| `PartyAssignmentEvent` | Audit row per newly-created assignment. |
| `Edge` | Polymorphic relationship. Indexes: unique `(from_id, to_id, rel_type)`, plus `(to_id, rel_type)` for inbound hops and walk propagation. |
| `GraphBulkLoadJob` | Shared with ingestion; Ownership's rows are keyed by run id (§7). |

## 9. Tests (`Thor.Workflows.Ownership.Tests`)

Testcontainers Postgres (Docker must be running), schema from
`EnsureCreatedAsync`.

| Suite | Covers |
|---|---|
| `Matching/OwnershipCandidateSqlTests` | Every built-in rule type, scopes, manifest scoping, caps, ranking — the branch's suite, now running the walk step first |
| `Walk/OwnershipWalkPropagatorTests` | Chains, owned intermediates, cycles, depth cap, same-run sources, the asset FK walk, a randomized DAG against the recursive oracle, resumption |
| `Steps/OwnershipVoteStepTests` | Stable windows, chunks not feeding each other, group and asset votes flushing |
| `Steps/OwnershipRunLifecycleStepTests` | start-run (open, link, seed, retry reset, validation), walk looping, finalize, both failure recorders |
| `Steps/OwnershipGraphLoadStepTests` | CSVs, reservation, retry, no winners, rejection, poll in-progress/completed/row errors, isolation from ingestion's job |
| `OwnershipWavePlannerTests` | Phase transitions and the next-wave JSON boundary, including the tolerated-chunk shape |
| `Persistence/*`, `Rules/*` | Writer idempotency, run identity, rule validation — unchanged from the branch |

## 10. Open risks / accepted tradeoffs / TODOs

1. **Existing tenants' schema.** The tenant schema is a regenerated
   `InitialTenantSchema` migration applied at provisioning. It is not
   established how `ownership_rule`, `ownership_vote`,
   `ownership_walk_candidate` and the new indexes reach tenants that already
   exist — the same open question as ATRE's `atre_vote`. Confirm before
   enabling anywhere with existing tenants.
2. **Superseded assignments are never deactivated**, and old `OWNED_BY`
   edges are never removed. With scope `all` an entity can hold several active
   rank-1 owners across runs, and Neptune accumulates an edge per run. Carried
   over from the branch; not fixed here.
3. **The ingestion-driven default scope** (`unassigned_and_confirmed`)
   re-matches human-confirmed entities and skips proposed ones. Kept as the
   branch had it; worth confirming it is intended.
4. **Concurrent executions.** As with ATRE, no execution `Name` is pinned, so
   an ingestion retry can start a second Ownership execution for the same
   manifest while the first is running; they share — and `start-run` resets —
   one workflow row.
5. **Unmeasured at scale.** No dial or query has been benchmarked against a
   real tenant. Frontier propagation removes the recursive walk's path
   blowup; the known remaining hotspots are the capped department fallback
   (the POC's 20M-row fan-out before its cap) and `sibling_match` with
   `similar`, a regex self-join that is quadratic in the table size. Offset
   windows also cost O(offset) each, as ATRE's do.
6. **Multi-level convergence is eventual, not same-run** — a group owned only
   through its grandparent resolves over successive scans.
7. **Seeding is insert-only** (§4.1).
8. **Two CyberArk-derived rules ship inactive**, and **two rule shapes are not
   representable** (a network-login-ID identity field; AD `description`/`info`)
   — unchanged from the branch.
9. **No Thor.Api endpoint** starts a tenant-wide run yet (§2.1).
10. **Edge-store coupling.** The walk depends on Postgres holding the full edge
    set. If edges ever move to Neptune only, `OwnershipWalkPropagator` is the
    piece to replace.

## 11. Key file reference

| Concern | File |
|---|---|
| Step keys / entry point | `OwnershipSteps.cs`, `Program.cs` |
| Request, scope resolution | `OwnershipRequest.cs`, `OwnershipScopes.cs` |
| Wave dials / planning | `OwnershipWaves.cs`, `OwnershipWavePlanner.cs`, `Models/OwnershipWave.cs` |
| Steps | `Steps/Ownership*Step.cs` |
| Walk propagation | `Walk/OwnershipWalkPropagator.cs` |
| Rule engine | `Rules/*` (§3) |
| Candidate query / window count | `Rules/OwnershipCandidateSqlBuilder.cs`, `Matching/OwnershipCandidateReader.cs` |
| Persistence, run identity | `Persistence/OwnershipResultWriter.cs`, `Persistence/OwnershipRunIdentity.cs` |
| State machine | `infra/src/modules/workflow/asl/ownership.asl.json` |
| Terraform entry | `infra/src/workflow_definitions.tf` (`ownership`) |
| Chaining from ingestion | `infra/src/modules/workflow/asl/ingestion.asl.json` (`StartOwnership`) |
| Neptune loader buckets | `infra/src/main.tf`, `var.neptune_bulk_load_workflows` |
