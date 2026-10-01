# ATRE Workflow

> Covers `backend/workflows/atre/Thor.Workflows.Atre/` end to end — deployment
> shape, rule schema, classification/voting algorithm, and persistence — plus
> the data model in `backend/shared/Thor.DataLayer` it reads and writes. One
> of the ADR §16 workflow modules: independent, additive, tenant-scoped,
> idempotent.

## 1. What ATRE does

Every account starts life with the default `Unclassified` account type when
it's promoted by ingestion. ATRE is what moves an account to a real type: for
each tenant run, it evaluates that tenant's active `account_type_rule` rows
against every in-scope account, and any rules that fire cast a weighted vote
for their target `account_type`. Votes are accumulated and the highest-total
type wins — see §5.

## 2. Deployment shape & entry points

Six workflow steps (`AtreSteps`), built into the one image
`backend/workflows/Dockerfile` produces for every workflow module
(`--build-arg WORKFLOW=Atre`):

| `THOR_STEP` | Step | Runs |
|---|---|---|
| `start-run` | `AtreStartRunStep` | once, before the first wave |
| `classify` | `AtreClassifyStep` | the Distributed Map body — once per chunk |
| `next-wave` | `AtreNextWaveStep` | once per wave |
| `finalize` | `AtreFinalizeStep` | once, after the last wave |
| `record-failure` | `AtreRecordFailureStep` | on the run's Catch path, when the run itself dies — see §6.1 |
| `record-chunk-failure` | `AtreRecordChunkFailureStep` | on a Map item's Catch path, once per lost chunk |

- `Program.cs` registers the step in a `THOR_STEP` → `IWorkflowStep` dictionary
  and hands it to `Thor.Workflows.Hosting.WorkflowEntryPoint.RunAsync`, which
  is where the choice between the two hosts lives — `AWS_LAMBDA_RUNTIME_API` is
  set by the Lambda execution environment and never under ECS, so
  `LambdaHost` runs when it is present and `EcsHost` otherwise. ATRE does not
  make that decision itself; no workflow module does.
- **ATRE is deployed on Lambda**, every step of it
  (`infra/src/workflow_definitions.tf`). Classification has no natural upper
  bound and Lambda's 15-minute ceiling cannot promise to fit it, so the scope
  is cut into chunks small enough that one fits — see §2.2. Each step returns
  `StepResult.Completed(value)` and the state machine reads it, which is what
  makes the loop possible at all.
- Each step derives from `WorkflowStep<T>`, so the platform does the
  deserialization and the step only implements
  `ExecuteAsync(T, CancellationToken)`. `T` is `AtreRequest` for `start-run`
  and `finalize`, `AtreChunkRequest` for `classify`, and `AtreNextWaveRequest`
  for `next-wave`.
- `AtreRequest(Guid TenantId, Guid? ScanManifestId = null, Guid? RunId = null)`
  is the one payload shape. `TenantId` is trusted as already verified upstream
  (per-connection tenant validation still runs regardless). `ScanManifestId`
  scopes the run to one scan's changed accounts; omitting it runs a full-table
  scan. `RunId` lets a caller pin an explicit idempotency key — see §6.
- The tenant DB routing chain comes from
  `Thor.Workflows.Hosting.Composition.TenantConnectionManagerFactory` (ADR
  §6.2/§6.3), shared with every other workflow module rather than copied per
  module.

### 2.1 How a run is started

Ingestion starts it. Its state machine's last state, `StartAtre`
(`infra/src/modules/workflow/asl/ingestion.asl.json`), uses the optimized
non-`.sync` `states:startExecution` integration: it returns as soon as ATRE's
execution begins and never learns whether it succeeded. Each workflow owns its
own failure handling — for ATRE that is its `workflow` row, not a DLQ; see §2.2.

The target ARN is built from the naming rule
(`thor-<env>-workflow-atre-sf`) rather than read from ATRE's module output —
declared as `chained_workflows = ["atre"]` on ingestion's entry in
`workflow_definitions.tf`, which is what produces both the
`${state_machine_arn_atre}` substitution and the `states:StartExecution` grant
scoped to exactly that ARN. Keeping it a string rather than a module reference
is what keeps a chain out of the Terraform dependency graph, and therefore what
would let `A → B` and `B → A` both be expressed without a cycle.

Two consequences worth knowing:

- **`Input` is projected field by field, not passed as `$.Request`.** Forwarding
  ingestion's `RunId` would make ATRE adopt it as its own, and
  `WorkflowLifecycle` looks a workflow row up by `RunId` alone when it has one
   — so ATRE would find and overwrite ingestion's row. Only `TenantId` and
  `ScanManifestId` cross.
- **No execution `Name` is pinned**, so an ingestion retry starts a second ATRE
  execution. That is safe because ATRE derives a deterministic `RunId` from
  `(TenantId, ScanManifestId)` and every write it makes is an
  `ON CONFLICT DO NOTHING` upsert keyed on it — see §6.

### 2.2 The wave loop

`infra/src/modules/workflow/asl/atre.asl.json`:

```
Envelope → StartRun → ClassifyWave (Distributed Map) → NextWave → MoreWaves
                           ↑                                          │ HasMore
                           └──────────────────────────────────────────┘
                                                                      │ default
                                                          Finalize → WorkflowSucceeded

any Task's Catch ──► RecordFailure ──► WorkflowFailed

inside the Map:  ClassifyChunk ──Catch──► RecordChunkFailure ──► ClassifyChunkTolerated
```

**ATRE has no dead-letter queue.** Nothing redrives one, it is started by
another workflow rather than by a queue, and a run that fails has a `workflow`
row of its own to say so. (The module still creates the SQS queue for every
enabled workflow — `dlq.tf` is ungated — but ATRE's definition never writes to
it. Making the queue itself optional is the plan's §2a, not yet built.)

A **chunk** is a window — an offset and a limit — and the `classify`
invocation reads its own rows for it. A Map item cannot be handed ten thousand
account ids, because Step Functions caps a state's payload at 256 KB. A
**wave** is `chunks_per_wave` chunks handed to the Map at once; the run does a
wave, waits for all of it, then comes back for the next, so the state machine
never holds more than one wave's worth of anything whatever the tenant's size.

**Nothing counts the scope up front.** `AtreWavePlanner.WaveAt` is arithmetic
over the wave index — it touches no database, so a run of any size starts at
the same cost. The run discovers it is finished by going: `NextWave` sums
`AccountsScanned` across the wave's chunk summaries, and a wave that comes back
short means the last chunk ran off the end (`HasMore = false`). It is a Lambda
rather than a `Pass` state only because ASL cannot sum an array.

Two dials, both environment configuration (`AtreWaves`, set from
`workflow_definitions.tf`) rather than constants, because the right values
depend on an environment's Aurora capacity:

| Dial | Default | Bounded by |
|---|---|---|
| `THOR_ATRE_ACCOUNTS_PER_CHUNK` | 10,000 | what fits inside the Lambda timeout |
| `THOR_ATRE_CHUNKS_PER_WAVE` | 5 | pair it with the Map's `MaxConcurrency` |

The Map's `MaxConcurrency` is a **connection** dial, not a throughput one:
each body opens a read and a write connection, so N concurrent bodies hold 2N
through the RDS Proxy.

**The Map has no `ItemSelector`, deliberately.** `StartRun` and `NextWave`
build each item complete, with the run's identity already in it. An
`ItemSelector` would have to reach for `$.Request.ScanManifestId` and
`$.Request.RunId`, and both are genuinely optional — ingestion starts ATRE with
a manifest and no run id — while a reference path that resolves to nothing
fails the whole execution with `States.Runtime`. Unlike `Choice`, which simply
does not match, an unresolvable path in `Parameters`/`ItemSelector` is fatal.
`AtreNextWaveStepTests` pins this by driving the step through the JSON boundary
with each caller's payload shape.

Results come back inline rather than through a `ResultWriter`, because
`NextWave` has to read the row counts.

**A failed chunk is tolerated — the run keeps classifying.** The Map item
catches to `RecordChunkFailure`, which writes the window it lost into the run's
`workflow.error`, then to a `ClassifyChunkTolerated` Pass. The other chunks in
the wave still finish and later waves still go out; the run ends
`completed_with_errors` rather than `completed`. The alternative — failing the
whole run on one bad chunk — is simpler but throws away every account the other
chunks would have classified.

Two things make that safe, and neither is optional:

- **A tolerated chunk counts as a *full* one** in the stop decision. It reports
  zero rows read, which by count alone is indistinguishable from a chunk that
  ran off the end of the accounts — so taking it at face value would end the run
  there and skip every account in every later wave, costing far more than the
  chunk that failed. `AtreChunkSummary.Failed` is what tells the two apart.
- **A wave where *every* chunk failed stops the run.** Assuming a full wave
  there would loop until the 25,000-event history cap. Stopping is not silent:
  the lost windows are already recorded.

The ceiling on one run is the Standard workflow execution-history cap of
25,000 events — roughly three thousand waves. Past that the answer is a bigger
wave or a bigger chunk, not a deeper loop.

## 3. Rule schema (`account_type_rule.rule_definition`)

A rule row (`backend/shared/Thor.DataLayer/Models/Tenants/AccountTypeRule.cs`)
carries `rule_definition` (a JSON condition tree, stored as `text`),
`applies_to` (only `"account"` today), `target_account_type_id`, and
`precision_score` — the vote's weight. Only `is_active` rules are loaded
(`AccountTypeRuleRepository.GetActiveAsync`) and parsed once per run.

The tree deserializes into `RuleNode` (`Rules/RuleNode.cs`) — a node is
**either** a leaf **or** a compound `and`/`or` of child nodes, distinguished
purely by which property is non-null (no discriminator tag):

```csharp
internal sealed class RuleNode
{
    public string? Field { get; set; }
    public string? Operator { get; set; }
    public JsonElement Value { get; set; }
    public List<RuleNode>? And { get; set; }
    public List<RuleNode>? Or { get; set; }
}
```

`RuleEvaluator.Evaluate` (`Rules/RuleEvaluator.cs`) recurses: `and` is
`.All()`, `or` is `.Any()`, nesting is unbounded, and arrays of any length
work identically to two — there's no hardcoded 2-item limit. By the time
`Evaluate` sees a node, `and`/`or` are guaranteed non-empty — see §4.

### Operators (`Rules/RuleOperator.cs`)

| Operator | Semantics |
|---|---|
| `equals` / `not_equals` | Exact match / its inverse, case-insensitive |
| `contains` / `starts_with` / `ends_with` | Ordinal substring/prefix/suffix match, case-insensitive |
| `is_null` / `is_not_null` | True when the resolved field is empty/absent, or present |
| `regex` | **Unanchored substring search** (`Regex.IsMatch`, not full-string match), 200ms timeout guard; an invalid pattern or a timeout both resolve to `false`, never throw |

Both sides of every comparison are lowercased first. `value` must be a JSON
string, bool, or number — an array/object silently resolves to `""` (not
rejected by validation, so it just won't match anything at evaluation time).

### Field resolution (`Rules/AccountFacts.cs`)

`field` resolves against known account columns first — `account_kind`,
`is_human`, `display_name`, `upn`, `email`, `domain_name` — falling back to
the account's `raw_attributes` JSON blob by the same key. Malformed
`raw_attributes` is treated as field-not-found, never thrown.

## 4. Accepted / rejected rule shapes

Validation (`backend/shared/Thor.Rules/AccountType/RuleNodeValidator.cs`) runs once per rule inside
`RuleFiringEngine.Parse`, after JSON deserialization but before the rule is
trusted for evaluation. An invalid rule is **skipped and logged as a
warning**, never fatal to the run — the same fail-safe treatment malformed
JSON gets.

**Accepted** — a leaf (`{"field","operator","value"}`), a non-empty `and`/`or`
of leaves, or arbitrarily deep/wide mixed nesting of both:

```json
{ "or": [
    { "and": [ { "field": "account_kind", "operator": "equals", "value": "user" },
               { "field": "dn", "operator": "contains", "value": "ou=serviceaccounts" } ] },
    { "field": "account_kind", "operator": "equals", "value": "computer" }
] }
```

**Rejected:**

| Shape | Why |
|---|---|
| `{ "and": [...], "or": [...] }` | Both compound keys on one node — ambiguous |
| `{}` | Neither `and`/`or` nor a `field` |
| `{ "and": [] }` / `{ "or": [] }` | Empty compound — no evaluable condition (rejected at validation; `Evaluate` never sees an empty list, so there's no "vacuous truth" case in this implementation) |
| `{ "field": "x", "operator": "bogus_op", "value": "y" }` | Operator not in `RuleOperator.All` |
| `{ "field": "x", "value": "y" }` | Leaf with no `operator` |
| A valid tree with one invalid leaf buried anywhere | Validation recurses into every child — one bad grandchild invalidates the whole rule |
| Invalid JSON | Fails at `JsonSerializer.Deserialize`, before validation runs |

## 5. Classification & voting algorithm

Per streamed account (`Steps/AtreClassifyStep.cs`):

1. **Evaluate every parsed rule** (`RuleFiringEngine.Evaluate`) — each rule
   runs in its own try/catch; a throwing rule is logged and treated as
   non-firing, never aborts the account or the run.
2. **Zero votes → no assignment.** The account is just counted
   (`AccountsWithNoFiringRule`); no `account_type_assignment` row is written.
3. **One or more votes → `VoteAccumulator.Decide`** (`Voting/VoteAccumulator.cs`):
   - Votes are summed **per target account type**, not per rule — multiple
     rules voting for the same type reinforce additively.
   - Highest total wins.
   - **Tie-break**: compare each tied type's single best-weighted
     contributing vote; if still equal, compare that vote's rule id as
     **ordinal hyphenated text** (matches Postgres `ORDER BY id::text`,
     deliberately not .NET's internal `Guid` byte ordering, for
     cross-platform reproducibility). A given rule id can only ever be the
     best vote for the one type it targets, so this always fully resolves.

**Worked example:** three active rules — R1 targets *Human* (weight 0.55),
R2 and R3 both target *Service Account* (weights 0.40, 0.30). An account that
matches all three gets Human = 0.55, Service Account = 0.40 + 0.30 = 0.70 —
**Service Account wins**, even though no single rule targeting it outweighs
R1 alone, because R2 and R3's votes for the same type add together. The
persisted assignment keeps the full `vote_distribution` (`{Human: 0.55,
ServiceAccount: 0.70}`) and all three `contributing_rule_ids`, so the losing
type's evidence stays auditable.

## 6. Persistence & idempotency

**Run identity** (`Persistence/AtreRunIdentity.cs`, `AtreRunIdentity.Derive`):
a thin ATRE-specific wrapper — carrying ATRE's own fixed namespace GUID — over
the shared `Thor.Workflows.Hosting.DeterministicRunId.Derive` algorithm
(moved out of this module to be shared across workflows): an
explicit `RunId` on the request always wins; else, given a `ScanManifestId`, a
deterministic RFC 4122 v5 (SHA-1) GUID over `"{tenantId}:{manifestId}"` under
that namespace GUID — so a retried run of the same manifest reuses the same
idempotency key; else (full-scan fallback, no manifest) a random GUID with a
logged warning that retries won't collapse. In practice this last branch's
*result* is never reached by `RunAsync` today — see §6.1: a request with
neither `ScanManifestId` nor `RunId` now fails fast at workflow-lifecycle
tracking before classification starts. The algorithm itself is covered by
`DeterministicRunIdTests` in `Thor.Workflows.Hosting.Tests`;
`AtreRunIdentityTests` now only checks that ATRE's wrapper delegates correctly.

**Writing results** (`Persistence/AtreResultWriter.cs`): decisions are staged
in memory and flushed in batches of 2000 accounts (`AtreClassifyStep.FlushBatchSize`),
plus once more at the end of the stream:

- `atre_vote` rows are always inserted (idempotent via the DB's unique
  `(entity_id, rule_id, run_id)` index — a re-run under the same `RunId`
  collapses, a different `RunId` for the same account still records new
  votes).
- `account_type_assignment` rows upsert via
  `ON CONFLICT (entity_id, entity_type) DO NOTHING`, returning only the
  entity ids actually newly inserted.
- **Only for those newly-inserted accounts** does the writer also add an
  `account_type_assignment_event` audit row and batch-stamp
  `account.account_type_id` (`IAccountRepository.BulkUpdateAccountTypeAsync`).

This atomic "only act on what you actually won the insert race for" pattern
is what makes two racing/retried flushes over overlapping accounts safe.

### 6.1 Workflow lifecycle tracking

`Thor.Workflows.Hosting.WorkflowLifecycle` (shared across workflow modules, not ATRE-specific)
creates/updates ATRE's row in the tenant `workflow` table. The calls are split across the
once-per-run steps, not made in the Map body: `AtreStartRunStep` calls `EnsureStartedAsync`,
`AtreFinalizeStep` calls `MarkCompletedAsync`. Many `classify` bodies run at once and none of them
knows whether it is the first or the last — ingestion's per-file Map body is split the same way, and
for the same reason. `WorkflowType = "atre"`, `Trigger = "step_functions"` when `ScanManifestId` is
given, else `"api"`.

`MarkFailedAsync` is called from the **Catch path**, by `AtreRecordFailureStep`, not from inside each
step's own `try`/`catch` the way every ingestion step does it. That is a deliberate difference, and
the reason is that ATRE's most likely failure is a chunk exceeding the Lambda timeout — **a killed
invocation runs no catch block**, so a step marking its own failure cannot cover the case it most
needs to. An out-of-memory kill and a Map item whose retries are exhausted are the same story. The
state machine is the only observer that sees all of them.

Every run-level `Catch` routes through it on the way to `WorkflowFailed`. Two properties matter:

- **It cannot swallow the failure.** `RecordFailure` has its own `Catch` to `WorkflowFailed` with
  `ResultPath: null`, so a DB blip while recording leaves the run failed anyway and the original
  error still in the execution history. Its retries are lighter than the rest of the definition
  (2 × 5s) because the run has already failed and every attempt only delays saying so.
- **A missing row is a no-op, not an error.** `MarkFailedAsync` returns silently when it finds
  nothing, which is the `StartRun`-failed-before-`EnsureStartedAsync` case: there is genuinely
  nothing to close.

This also keeps `AtreNextWaveStep` pure arithmetic with no database of its own.

### 6.2 A chunk failing, versus the run failing

These are different events with different handling, and the distinction is the whole of ATRE's
failure design:

| | A chunk fails | The run fails |
|---|---|---|
| Caught by | the Map item's `Catch` | the run-level `Catch` on any Task |
| Recorded by | `AtreRecordChunkFailureStep` | `AtreRecordFailureStep` |
| Writes | appends the lost window to `workflow.error` | `status = failed`, plus the cause |
| Then | the run **carries on** | the run ends at `WorkflowFailed` |
| Final status | `completed_with_errors` (set by `Finalize`) | `failed` |

`AtreRecordChunkFailureStep` appends with a single `UPDATE`
(`IWorkflowRepository.AppendErrorAsync`), not a read-then-write: a wave's chunks fail concurrently on
separate invocations, and read-modify-write would drop all but one of their messages. It deliberately
leaves `status` alone — the run is still in flight, and a non-empty `error` column is exactly the
signal `AtreFinalizeStep` reads at the end to choose between `completed` and `completed_with_errors`.

If `RecordChunkFailure` itself fails, its `Catch` still tolerates the item. That is the worst case
here — the window is lost with only the CloudWatch log to show for it — and is why that state retries
at all. Failing the run instead would undo the tolerating the design exists to provide.

The row is looked up by `RunId` when the request supplies one, else by `(ScanManifestId,
WorkflowType)` — see `ingestion_workflow.md` §10.1 for the full retry-vs-retrigger explanation of
why `RunId` exists and how it's expected to be minted/reused by whatever triggers a run. ATRE
reuses `AtreRequest.RunId` — the **same field** `AtreRunIdentity.Derive` already reads for
vote-persistence idempotency — but the two consumers are independent: `WorkflowLifecycle` gets the
raw `request.RunId` (possibly `null`), while `AtreResultWriter`'s idempotency key is whatever
`AtreRunIdentity.Derive` computes from it (which may differ, e.g. for the scan-driven case it's a
deterministic hash, not the raw value).

`WorkflowLifecycle.EnsureStartedAsync` throws if both `ScanManifestId` and `RunId` are absent —
fail closed, since there'd be no way to identify the row on a later
`MarkCompletedAsync`/`MarkFailedAsync` call. `ScanId` is left `null` on ATRE's workflow rows
(unlike ingestion's, which always has one) — `AtreRequest` never carried a `ScanId`, and adding a
`ScanManifest` lookup solely to populate this optional column wasn't worth the new dependency.

When `ScanManifestId` is present (scan-driven case), `AtreStartRunStep` also links its `workflow`
row as a child of the ingestion run for that same manifest via a `workflow_graph` row
(`ParentWorkflowId` = ingestion's workflow id, `ChildWorkflowId` = ATRE's), looked up by
`(ScanManifestId, WorkflowType = "ingestion")`. This runs once, right after `EnsureStartedAsync`,
and is idempotent — a retry that reuses the same `RunId` finds the edge already exists and skips
re-inserting it. If no ingestion row is found for the manifest (e.g. an ordering edge case), ATRE
logs a warning and continues rather than failing the run — lineage is observability, not
correctness-critical for classification itself. Standalone/API-triggered runs (`ScanManifestId`
absent) never get a `workflow_graph` row, since there's no parent to link to.

## 7. Streaming (`Streaming/AccountStreamReader.cs`)

Accounts are streamed via `AsNoTracking().AsAsyncEnumerable()`, never
materialized into memory. Manifest-scoped mode filters to accounts referenced
by that manifest's `IngestChangeEvent` rows; omitting the manifest streams
every account (full scan).

One call reads **one chunk's window**, not the whole scope:
`Skip(offset).Take(limit)` over `ORDER BY id`. The ordering is what makes a
run's chunks disjoint and complete — it is total and stable, so `[0, 10000)`
and `[10000, 20000)` can neither overlap nor leave a gap. `id` specifically,
because it is immutable; ordering by anything ATRE itself writes would not be
stable, since a run updates `account.account_type_id` as it goes. A window
past the end yields nothing, which is how a run discovers it is finished.

`AtreClassifyStep` opens **two independent** `TenantDbContext` connections —
one for the reader, one for the writer — so committing writes never disturbs
the read side's streaming snapshot.

## 8. Data model (`backend/shared/Thor.DataLayer/Models/Tenants/`)

| Model | Purpose |
|---|---|
| `AccountTypeRule` | A rule row: `rule_definition`, `target_account_type_id`, `precision_score` (weight), `is_active`. Precision-tracking columns (`total_predictions`/`true_positive_count`/`false_positive_count`) exist but are **not written by ATRE itself**. |
| `AtreVote` | One rule's vote for one account in one run. Unique on `(entity_id, rule_id, run_id)`. |
| `AccountTypeAssignment` | The current type for an entity (polymorphic `entity_type`/`entity_id`, so groups could be covered later). Unique on `(entity_id, entity_type)`. Carries `vote_distribution`, `contributing_rule_ids`, `precision_score_snapshot` (all JSON/array snapshots of the decision), and `method` (`"rule"` for ATRE, vs. `"default"`/`"manual"`). |
| `AccountTypeAssignmentEvent` | Audit trail row per newly-created assignment: previous/new type, actor (`"atre_workflow"`), event type (`"assigned"`). |

## 9. Key file reference

| Concern | File |
|---|---|
| Entry point / compute dispatch | `Program.cs`, `AtreSteps.cs`, `Thor.Workflows.Hosting/WorkflowEntryPoint.cs` (shared) |
| Request shape | `AtreRequest.cs` |
| Tenant DB routing (shared) | `Thor.Workflows.Hosting/Composition/TenantConnectionManagerFactory.cs` |
| Run bookkeeping (open / close the `workflow` row) | `Steps/AtreStartRunStep.cs`, `Steps/AtreFinalizeStep.cs` |
| Closing the row on failure (the Catch path) | `Steps/AtreRecordFailureStep.cs` |
| Recording one tolerated chunk's lost window | `Steps/AtreRecordChunkFailureStep.cs` |
| Terminal statuses beyond the generic ones | `Constants/AtreStatuses.cs` |
| Classification of one chunk (the Map body) | `Steps/AtreClassifyStep.cs` |
| Wave arithmetic and the loop's stop condition | `AtreWavePlanner.cs`, `Steps/AtreNextWaveStep.cs` |
| Wave/chunk dials and their defaults | `AtreWaves.cs` |
| Account streaming (one chunk's window) | `Streaming/AccountStreamReader.cs` |
| Condition tree / operators / field resolution | `Rules/RuleNode.cs`, `Rules/RuleOperator.cs`, `Rules/AccountFacts.cs` |
| Tree evaluation | `Rules/RuleEvaluator.cs` |
| Shape validation (accept/reject) | `backend/shared/Thor.Rules/AccountType/RuleNodeValidator.cs` |
| Parse + per-account firing | `Rules/RuleFiringEngine.cs` |
| Weighted vote accumulation + tie-break | `Voting/VoteAccumulator.cs` |
| Run identity (ATRE wrapper) | `Persistence/AtreRunIdentity.cs` |
| Run identity algorithm (shared) | `Thor.Workflows.Hosting/DeterministicRunId.cs` |
| Result persistence + idempotency | `Persistence/AtreResultWriter.cs` |
| Workflow lifecycle tracking (shared) | `Thor.Workflows.Hosting/WorkflowLifecycle.cs` |
| Wave / chunk / summary shapes | `Models/AtreWave.cs`, `Models/AtreChunkSummary.cs` |
| Catch-payload shape | `Models/AtreFailure.cs` |
| Rule row schema | `backend/shared/Thor.DataLayer/Models/Tenants/AccountTypeRule.cs` |
| State machine definition | `infra/src/modules/workflow/asl/atre.asl.json` |
| Steps, compute and how ingestion chains to it | `infra/src/workflow_definitions.tf` |
| Tests: evaluation & nesting | `Thor.Workflows.Atre.Tests/Rules/RuleEvaluatorTests.cs` |
| Tests: shape validation | `Thor.Workflows.Atre.Tests/Rules/RuleNodeValidatorTests.cs` |
| Tests: parse-level skip behavior | `Thor.Workflows.Atre.Tests/Rules/RuleFiringEngineTests.cs` |
| Tests: voting & tie-breaks | `Thor.Workflows.Atre.Tests/Voting/VoteAccumulatorTests.cs` |
| Tests: run identity wrapper | `Thor.Workflows.Atre.Tests/Persistence/AtreRunIdentityTests.cs` |
| Tests: run identity algorithm (shared) | `Thor.Workflows.Hosting.Tests/DeterministicRunIdTests.cs` |
| Tests: persistence idempotency | `Thor.Workflows.Atre.Tests/Persistence/AtreResultWriterTests.cs` |
| Tests: streaming scope | `Thor.Workflows.Atre.Tests/Streaming/AccountStreamReaderTests.cs` |
| Tests: end-to-end classify step | `Thor.Workflows.Atre.Tests/Steps/AtreClassifyStepTests.cs` |
| Tests: wave arithmetic | `Thor.Workflows.Atre.Tests/AtreWavePlannerTests.cs` |
| Tests: the loop's JSON boundary with the state machine | `Thor.Workflows.Atre.Tests/Steps/AtreNextWaveStepTests.cs` |
| Tests: run open / close bookkeeping | `Thor.Workflows.Atre.Tests/Steps/AtreStartRunStepTests.cs`, `Steps/AtreFinalizeStepTests.cs` |
| Tests: the failure path closes the row | `Thor.Workflows.Atre.Tests/Steps/AtreRecordFailureStepTests.cs` |
| Tests: a tolerated chunk's window is recorded | `Thor.Workflows.Atre.Tests/Steps/AtreRecordChunkFailureStepTests.cs` |
