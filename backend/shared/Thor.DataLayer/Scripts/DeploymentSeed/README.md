# DeploymentSeed

`.sql` scripts here are run against the Master DB by the `Thor.MasterDbSeed` Lambda
(`backend/functions/Thor.MasterDbSeed`), in filename order, inside a single transaction.

- **Naming:** `NNN_description.sql`, zero-padded (e.g. `001_seed_authentication_types.sql`) —
  the numeric prefix is what determines execution order.
- **Idempotent only:** the Lambda re-runs every script on every invocation (Terraform re-invokes
  it whenever the build output changes, which includes any script add/edit). Write each script
  so a repeat run is a safe no-op, e.g. `INSERT ... ON CONFLICT DO NOTHING` / upsert — never a
  plain `INSERT` that would duplicate rows on re-run.
- **Scope:** the Lambda connects as `thor_master_seed`, a least-privilege IAM role (see
  `backend/functions/Thor.DbBootstrap/src/Thor.DbBootstrap.Function/db-roles.sql`) with
  `SELECT, INSERT, UPDATE` on `master.authentication_types`, `master.connector_config_fields`,
  `master.authentication_fields`, `master.connector_types`, and `auth.api_scopes` only. A script
  targeting any other table needs that role's grants extended first.
