# DB role bootstrap

The least-privilege database roles the platform assumes at runtime are created by the
**`Thor.DbBootstrap` Lambda** (`backend/functions/Thor.DbBootstrap`), not by a manual SQL run.
Terraform's `db_bootstrap` module (`infra/src/modules/db_bootstrap`) deploys that Lambda in-VPC
and invokes it at `apply` (via `aws_lambda_invocation`). There is **no** checked-in `db-roles.sql`
to run by hand and **no** out-of-band step. The role/grant script is embedded in the Lambda
(`.../Thor.DbBootstrap.Function/db-roles.sql`).

| Role | Purpose | Grants |
|------|---------|--------|
| `thor_provisioner` | Tenant-provisioning **DDL only** (create-tenant-database step) | `LOGIN CREATEDB CREATEROLE`, `rds_iam WITH ADMIN OPTION` |
| `thor_metadata_writer` | Write tenant metadata (seed + finalize-routing steps) | `LOGIN`, `rds_iam`, write on the two `auth` tables, read on `master` |
| `thor_app` | Runtime read of tenant routing | `LOGIN`, `rds_iam`, read on `auth` + `master` |
| `thor_authorizer` | Authorizer's subdomain→routing lookup (in-VPC, IAM via the RDS Proxy) | `LOGIN`, `rds_iam`, read on `auth.tenant` + `auth.tenant_routing` |

**Every role uses RDS IAM auth — there is no password anywhere.** The authorizer now runs in-VPC
and connects through the RDS Proxy with an IAM token as `thor_authorizer` (the RDS Data API is
disabled, `enable_http_endpoint = false`). Per-tenant `tenant_<id>_rw`/`_ro` roles are created
later by the provisioning workflow, not here.

## How it runs

- **Master credentials, in-VPC only.** The bootstrap Lambda is the *only* place the AWS-managed
  Aurora master credential is used. It reads that secret (by ARN) through the Secrets Manager VPC
  interface endpoint and opens **one** direct connection to the Aurora writer as master. Nothing
  outside the VPC ever touches the DB.
- **Idempotent + self-triggering.** The embedded script uses `DO`-block existence checks for the
  roles and `to_regclass(...)` guards for the table/schema grants, so it is safe to run before
  **or** after the Master migrations and safe to re-run. The `aws_lambda_invocation` re-triggers on
  the Lambda's `source_code_hash`, so any change to the roles/grants redeploys and re-applies
  automatically on the next `apply`.

## Migration ordering

Role creation always succeeds. The table/schema **grants** only take effect once the Master
migrations have created `auth.tenant` / `auth.tenant_routing` (and the `master` schema). On a
brand-new cluster, run the migrations, then let the next `apply` re-invoke the bootstrap (or change
the script) so the guarded grants apply. Automated migration apply is a separate, not-yet-wired
step.
