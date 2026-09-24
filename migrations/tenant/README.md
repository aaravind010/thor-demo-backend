# Tenant schema migrations

Changes the schema of **existing** tenant databases when the EF `TenantDbContext` model changes.
New tenants are unaffected: `Thor.TenantProvisioning` still creates them from the current model.

Tenant schema changes follow **Expand → Migrate → Contract**. This pipeline implements the
**expand** phase (Part 1); migrate (backfills) and contract (drops, renames, type changes) are Part 2.

| Phase | Changes | Runs | Status |
|---|---|---|---|
| Expand | new tables, new columns (nullable or constant default), their indexes/FKs | **before** the app deploy — gates it | this pipeline |
| Migrate | backfill / copy data | between releases | Part 2 |
| Contract | drop / rename columns and tables, tighten nullability, change types | **after** all services stop using the old shape | Part 2 |

An expanded schema is always backward compatible (EF only selects the columns it maps), so the
old app keeps running on it and an app rollback never needs a schema rollback.

## How a change is detected

1. **Desired state = the EF model.** `dotnet ef dbcontext script` emits the DDL of
   `TenantDbContext` via `TenantDbContextDesignTimeFactory` (no DB connection); CI normalizes it
   into `desired.hcl` with Atlas and a throwaway Postgres 16 service container (`atlas.hcl`).
2. **Skip** if `sha256(desired.hcl)` equals `state/applied.json` in the migration bucket.
3. **Otherwise** each selected tenant's live schema is snapshotted and diffed against
   `desired.hcl` — which also surfaces manual drift.

## Flow

Atlas can only diff with an empty Postgres "dev database", and ECS has none. So the ECS runner only
**inspects** (snapshots live schemas) and **applies**; CI, which has a dev database, **plans**.

```
tenant-migrations.yml                      thor-<env>-tenant-migration (Step Functions)
  generate  desired.hcl, image, task def
  plan      start execution ───────────▶  Inspect (ECS, MODE=inspect) → runs/<id>/live/*.hcl
            plan.sh: diff + classify  ◀──  WaitForPlanAndApproval (token in runs/<id>/approval-token.json)
            → plans/, summary, manifest
            blocking → send-task-failure ▶ fail
            no changes → send-task-success ▶ MarkApplied
  approve   <env>-db-migration reviewers
            send-task-success ─────────▶  Apply (Distributed Map, ECS MODE=apply per tenant)
  apply-wait                               MarkApplied (state/applied.json, fleet runs only)
```

Apply only runs SQL that was approved: the plan object must hash to the approved plan, and the
tenant's live schema must still hash to the snapshot CI planned against (drift fails the tenant).

`deploy.yml` calls this workflow (`tenant: all`) and every service deploy job needs it, so a
failed plan, rejected approval or failed apply blocks the deploy.

## What gets applied (classify.sh)

Each tenant gets two diffs in `plan.sh`: the **expand** diff (`atlas.hcl`'s `expand` env skips every
drop/modify) is the plan; the **full** diff shows what expand left out.

| Class | Examples | Result |
|---|---|---|
| ✅ expand | `CREATE TABLE`, `ADD COLUMN … NULL`, `ADD COLUMN … NOT NULL DEFAULT <constant>`, `CREATE INDEX`, `ADD … FOREIGN KEY` | applied after approval |
| ⛔ blocking | `ADD COLUMN … NOT NULL` without default; volatile default (`gen_random_uuid()`); removed property whose column is `NOT NULL` without default; property made nullable; suspected rename (add + drop in one table) | plan fails, deploy blocked |
| 🕓 deferred | other drops, type changes, `SET NOT NULL` | reported as *pending contract*, not applied |

## Developer rules (Part 1)

- New columns must be **nullable** or have a **constant default**.
- **Don't rename** a property or column — that's add + backfill + drop across releases (Part 2).
- Removing a property is safe only if its column is already nullable; the column stays in the DB
  until the contract phase.
- Objects created outside EF (raw SQL) show up as drops — add them to the model instead.

## Running it

- **Automatically:** every push deploy (`deploy.yml`) runs it for all tenants; it's a no-op when
  the desired state is already applied.
- **Manually:** *Actions → Tenant Migrations → Run workflow*, with `environment` and `tenant`
  (`all` or one `tenant_id`). A single-tenant run never marks the fleet as applied.
- **PRs** touching the tenant model run `generate` only: desired state + classifier tests.

Classifier tests locally: `sh migrations/tenant/classify_test.sh`.

## Runbook

| Situation | Action |
|---|---|
| Plan failed with blocking findings | Fix the model per the rules above; re-run. The job summary lists every finding. |
| Apply failed for a tenant | That tenant was rolled back (single transaction). Fix the cause and re-run — tenants already migrated have an empty diff and are skipped. |
| `changed since it was planned` | The tenant's schema changed between plan and apply. Re-run to re-plan. |
| Approval rejected, or planning failed | The `cancel` job stops the execution; nothing was applied. |
| Execution parked on WaitForPlanAndApproval | It fails after `approval_timeout_seconds` (24h), or stop it from the console. |

## Files

| Path | Purpose |
|---|---|
| `atlas.hcl` | CI: `ef` env (EF model → `desired.hcl`) and `expand` env (diff policy) |
| `plan.sh` | CI: diff every tenant snapshot, classify, write plans/summary/manifest |
| `Dockerfile`, `entrypoint.sh` | Runner image (Atlas + psql + AWS CLI) |
| `inspect.sh`, `apply.sh`, `lib.sh` | Runner's inspect and apply modes; shared helpers |
| `classify.sh`, `classify_test.sh` | Expand gate and its tests (one case per rule) |
| `statemachine/tenant-migration.asl.json` | State machine definition |
| `infra/src/modules/tenant_migration` | Bucket, ECR, ECS, state machine, IAM |
| `.github/workflows/tenant-migrations.yml` | Pipeline |

S3 layout (`thor-<env>-tenant-migration-<account>`, accessible only to the GitHub OIDC deploy role,
the runner task role and the state machine role):

```
runs/<runId>/tenants.json | live/<tenantId>.hcl | live/<tenantId>.notnull.txt   (runner: inspect)
             plans/<tenantId>.sql | summary.json | manifest.json | plan-result.json (CI: plan)
             approval-token.json | results/<tenantId>.json
state/applied.json | state/tenants/<tenantId>.json
```
