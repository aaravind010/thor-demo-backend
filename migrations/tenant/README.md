# Tenant schema migrations

Changes the schema of **existing** tenant databases when the EF `TenantDbContext` model changes.
New tenants are unaffected: `Thor.TenantProvisioning` still creates them from the current model.

Tenant schema changes follow **Expand → Migrate → Contract**. Each pipeline run does one phase:

| Phase | Changes | Runs |
|---|---|---|
| Expand | new tables, new columns (nullable or constant default), their indexes/FKs; `DROP NOT NULL` | **before** the app deploy — automatic, gates it |
| Migrate | backfill scripts: copy or fill data | **after** the app deploy — automatic when a script is pending |
| Contract | drop old columns/tables, `SET NOT NULL`, default and index changes | **by hand**, once the old shape is out of use (see [Contract](#contract-dropping-the-old-shape)) |

An expanded schema is always backward compatible (EF only selects the columns it maps), so the
old app keeps running on it and an app rollback never needs a schema rollback.

## Changing the model safely

| I want to… | How |
|---|---|
| Add a table, or a column that is nullable or has a constant default | Change the model. Expand adds it before the deploy. |
| Make a column optional | Change the model. Expand drops `NOT NULL` before the deploy. |
| Remove a property | Remove it. Expand makes its column nullable; Contract drops the column later. |
| **Rename** a column | ① Add the new property; the code writes both, still reads the old one; add a backfill script that copies old → new. ② Read the new property and remove the old one. ③ Run Contract once a later model change is deployed — it drops the old column. |
| **Change a column's type** | Same as a rename: a new column with the new type, a backfill, then drop the old one. |
| **Make a column required**, or add one that is `NOT NULL` without a constant default | ① Add it nullable; the code always writes it; add a backfill script that fills existing rows. ② Mark it required. Contract applies `SET NOT NULL` once a later model change is deployed. |

- **Every writer writes both columns during a transition** — Thor.API, Thor.TaskAPI, the ingestion
  workflows and the Lambdas all build from `backend/shared` and must all dual-write. The pipeline
  cannot check this.
- **One change per table per PR:** adding a column and removing another from the same table in one
  PR looks like a rename and is blocked. Split it into two PRs.
- Objects created outside EF (raw SQL) show up as drops — add them to the model instead.

## How a change is detected

- **Expand.** The desired state is the EF model: `dotnet ef dbcontext script` emits the DDL of
  `TenantDbContext` via `TenantDbContextDesignTimeFactory` (no DB connection); CI normalizes it into
  `desired.hcl` with Atlas and a throwaway Postgres 16 service container (`atlas.hcl`). Skipped when
  `sha256(desired.hcl)` equals `state/applied.json`; otherwise each selected tenant's live schema is
  snapshotted and diffed against `desired.hcl` — which also surfaces manual drift.
- **Migrate.** The scripts in `backfills/` against each tenant's record of scripts already run
  (`tenant.__thor_backfills`). Skipped when there are no scripts, or when every tenant has run this
  exact set (`state/backfills-applied.json`).
- **Contract.** The items expand leaves out (*pending contract*), limited to those old enough to go.

## Flow

Atlas can only diff with an empty Postgres "dev database", and ECS has none. So the ECS runner only
**inspects** (snapshots live schemas) and **applies**; CI, which has a dev database, **plans**.

```text
deploy.yml (push)   tenant-migrations (expand) ─▶ deploy-* services ─▶ tenant-backfills (migrate)
Run workflow        any phase, all tenants or one  (contract only runs this way)

tenant-migrations.yml                      thor-<env>-tenant-migration (Step Functions)
  generate  checks, desired.hcl, image, task def; skip / record the deploy (per phase)
  plan      start execution ───────────▶  Inspect (ECS, MODE=inspect) → runs/<id>/live/*
            plan.sh <phase>           ◀──  WaitForPlanAndApproval (token in runs/<id>/approval-token.json)
            → plans/, summary, manifest
            blocking → send-task-failure ▶ fail
  approve   "Approve <phase>": <env> environment reviewers (every run, even an empty plan)
            send-task-success ─────────▶  HasChanges → Apply (Distributed Map, ECS MODE=apply per tenant)
  apply-wait                               fleet runs only: expand  → state/pending-contract.json, applied.json
                                                            migrate → state/backfills-applied.json
```

Apply only runs SQL that was approved: the plan object must hash to the approved plan, and the
tenant's live schema must still hash to the snapshot CI planned against (drift fails the tenant).
Each tenant's plan runs in one transaction (`search_path = tenant`, `lock_timeout 5s`,
`statement_timeout 15min`); a failure rolls that tenant back and fails the run.

Every service deploy job in `deploy.yml` needs expand, so a failed plan, rejected approval or failed
apply blocks the deploy. Migrate runs after the deploys: a failed or rejected migrate leaves the
services live (the schema already fits them) and blocks Contract for those tenants until it succeeds.

## Expand: what gets applied (classify.sh)

Each tenant gets two diffs in `plan.sh`: the **expand** diff (`atlas.hcl`'s `expand` env skips every
drop/modify) and the **full** diff, which shows what expand left out. The plan is `classify.sh`'s
expand list: the expand diff plus the `DROP NOT NULL`s taken from the full diff.

| Class | Examples | Result |
|---|---|---|
| ✅ expand | `CREATE TABLE`, `ADD COLUMN … NULL`, `ADD COLUMN … NOT NULL DEFAULT <constant>`, `CREATE INDEX`, `ADD … FOREIGN KEY`; `DROP NOT NULL` for a property made nullable or a removed property whose column is `NOT NULL` | applied after approval |
| ⛔ blocking | `ADD COLUMN … NOT NULL` without default; volatile default (`gen_random_uuid()`); column type change; suspected rename (an add and a new drop in one table — a column already pending contract from an earlier release doesn't count) | plan fails, deploy blocked |
| 🕓 deferred | drops, `SET NOT NULL`, default and index changes | reported as *pending contract*, not applied |

## Migrate: writing a backfill script

```sql
-- requires: scan.old_name, scan.new_name
UPDATE scan SET new_name = old_name WHERE new_name IS NULL AND old_name IS NOT NULL;
```

- **Name:** `backfills/NNNN_<name>.sql` (letters, digits, `_`). Scripts run in number order; the
  file name is the script's id.
- **`-- requires:`** lists every column the script reads or writes. A tenant missing one of them
  never had the old shape (it was created later), so the script is skipped there. Add the script in
  the release whose model has all of them — CI fails the PR otherwise, since a typo would skip the
  script everywhere and let Contract drop data it never copied.
- **Safe to run again** (`WHERE new_name IS NULL`).
- **Data only:** `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `WITH`, `DO` and `CREATE TEMP TABLE`.
  No DDL, no `BEGIN` / `COMMIT` / `ROLLBACK` / `SET ROLE`, no psql `\` commands, and every statement
  ends with `;`. Unqualified names resolve to the `tenant` schema.
- **Never edit a script after it ran** anywhere (`backfill-changed`) — add a new one.
- **Delete it** only once its old column has been dropped from every tenant; from then on it is
  skipped everywhere anyway.

All of a tenant's pending scripts run in its one transaction, each followed by its record row in
`tenant.__thor_backfills` (id, hash, time — Atlas ignores this table). Two guards keep scripts to
data: CI checks the text (a PR fails early with file and line), and the plan fingerprints the schema
before the scripts and checks it after each one (`schema_fingerprint.sql`), which also catches DDL
hidden in `DO … EXECUTE` — the tenant rolls back, record row included. A tenant must be fully
expanded first (`not-expanded`).

## Contract: dropping the old shape

Contract applies the *pending contract* items — but only those the release you would roll back to no
longer uses. **Rule:** an item goes once a *later* model version than the one it first appeared in
has been deployed.

| Deployed model | `old_name` (removed from the model in M2) |
|---|---|
| M2 | ⏳ waits — rolling back to M1 would still need it |
| M3 (any later model change) | ✅ dropped after approval |

The pipeline keeps the record: fleet expand runs save when each item first appeared
(`state/pending-contract.json`); the migrate run after every push deploy saves the deployed model
(`state/deployed.json`).

- **Manual only:** *Run workflow* with `phase: contract`, all tenants or one. Run it on the deployed
  branch — it refuses any other model.
- A tenant must be fully expanded and have **no pending backfills** (`backfill-pending`).
- An item with no record of when it appeared (e.g. a column added by hand) waits.
- The summary lists per tenant: ✅ drop now, ⏳ waiting (and why), ⛔ blocking.

## Running it

- **Push deploy (`deploy.yml`):** expand for all tenants before the services deploy, migrate for all
  tenants after them. Either finishes in `generate` when there is nothing to do — except that expand
  runs (and asks for approval, usually with an empty plan) until `state/pending-contract.json` exists,
  so the first deploy records what is already pending contract.
- **By hand:** *Actions → Tenant Migrations → Run workflow* with `environment`, `tenant` (`all` or one
  `tenant_id`) and `phase`. A single-tenant run neither reads the fleet records to skip nor writes
  them.
- **PRs** touching the tenant model or this folder run `generate` only: desired state, planner tests
  and the backfill script check.
- Every run waits for **"Approve \<phase\>"** on the `<env>` environment, even with an empty plan.
  One run per environment at a time; a running one is never cancelled.

Planner tests locally: `sh migrations/tenant/classify_test.sh`, `backfills_test.sh`, `contract_test.sh`.

## Runbook

| Situation | Action |
|---|---|
| Plan failed with blocking findings | Fix per the rules above and re-run. The job summary lists every finding. |
| `suspected-rename` | Follow the rename recipe, or split the PR if it isn't a rename. |
| `type-change` | Follow the type-change recipe. |
| `backfill-ddl`, `backfill-psql-command`, `backfill-unterminated`, `backfill-no-requires`, `backfill-bad-name` | Fix the script as the message says. |
| `backfill-changed` | The script was edited after it ran — restore it and add a new script. |
| `backfill changed the schema` (apply failed) | The script contains DDL, maybe inside a `DO` block. The tenant was rolled back; move the DDL into the model. |
| `not-expanded` | That tenant still has expand work — run expand first. |
| `backfill-pending` (contract) | Run migrate first. |
| ⏳ `too-new` (contract) | Expected: it goes after the next model change is deployed. |
| Contract refused: `only against the deployed model` | Run it on the deployed branch. `none recorded yet` means no push deploy has run migrate yet. |
| `SET NOT NULL` failed for a tenant (contract) | Rows are still NULL — fix the backfill, run migrate, then contract again. |
| Apply failed for a tenant | That tenant was rolled back (single transaction). Fix the cause and re-run — tenants already done have nothing left to apply. |
| `changed since it was planned` | The tenant's schema changed between plan and apply. Re-run to re-plan. |
| Approval rejected, or planning failed | The `cancel` job stops the execution; nothing was applied. |
| A deploy's expand is waiting to start | Another run for that environment is still open — often a migrate waiting for approval. Approve or reject it. |
| Execution parked on WaitForPlanAndApproval | It fails after `approval_timeout_seconds` (24h), or stop it from the console. |

## Known limits

- Lambdas and workflows deploy through `infra.yml`, which Contract cannot see — run Contract only when
  that pipeline is green for the same commit.
- The rollback window covers one release. After a manual deploy or rollback (`deploy-manual`), wait
  for the next push deploy before running Contract.
- A tenant's backfills must finish within the 15-minute statement timeout.
- Columns added by hand show up as pending contract and can be dropped by Contract — check the summary.

## Files

| Path | Purpose |
|---|---|
| `atlas.hcl` | CI: `ef` env (EF model → `desired.hcl`) and `expand` env (diff policy) |
| `plan.sh` | CI: plan one phase for every tenant snapshot; write plans/summary/manifest |
| `Dockerfile`, `entrypoint.sh` | Runner image (Atlas + psql + AWS CLI) |
| `inspect.sh`, `apply.sh`, `lib.sh` | Runner's inspect and apply modes; shared helpers |
| `classify.sh`, `classify_test.sh` | Expand gate and its tests (one case per rule) |
| `backfills.sh`, `backfills_test.sh` | Migrate gate: which backfill scripts a tenant still needs; data statements only |
| `contract.sh`, `contract_test.sh` | Contract gate: which pending-contract items are old enough to apply |
| `schema_fingerprint.sql` | Migrate plans' DDL guard: fails the tenant's transaction if a backfill changed the schema |
| `backfills/` | Backfill scripts (created with the first one) |
| `statemachine/tenant-migration.asl.json` | State machine definition |
| `infra/src/modules/tenant_migration` | Bucket, ECR, ECS, state machine, IAM |
| `.github/workflows/tenant-migrations.yml` | Pipeline; `deploy.yml` calls it before and after the service deploys |

S3 layout (`thor-<env>-tenant-migration-new-<account>`: private, no bucket policy — access comes from
each principal's IAM permissions):

```text
runs/<runId>/tenants.json | live/<tenantId>.{hcl,notnull.txt,columns.txt,backfills.tsv}   (runner: inspect)
             plans/<tenantId>.sql | summary.json | manifest.json | plan-result.json
             pending-contract.json                                                   (CI: plan)
             approval-token.json | results/<tenantId>.json
state/applied.json             desired state every tenant has (fleet expand runs)
state/pending-contract.json    when each pending-contract item first appeared (fleet expand runs)
state/backfills-applied.json   backfill set every tenant has run (fleet migrate runs)
state/deployed.json            model the services run (migrate run after each push deploy)
state/tenants/<tenantId>.json  last plan applied to the tenant
```
