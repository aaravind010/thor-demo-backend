-- One-time cluster bootstrap: the least-privilege DB roles the platform assumes at runtime.
--
-- Chicken-and-egg: creating login roles and granting rds_iam needs an admin identity once. This
-- script is run by the Thor.DbBootstrap Lambda as the Aurora master user (AWS-managed secret,
-- read in-VPC via Secrets Manager) over a direct connection to the writer. It is idempotent, so
-- Terraform re-invokes it safely whenever the role set changes.
--
-- Auth is RDS IAM (no password) for EVERY role — including thor_authorizer, which now connects
-- in-VPC through the RDS Proxy with an IAM token (the RDS Data API is disabled). There is no
-- password anywhere in the platform.
--
-- Least privilege by role — each identity gets only what its job needs:
--   thor_provisioner      DDL only: create tenant databases + their roles
--   thor_metadata_writer  write tenant + tenant_routing rows (no DDL power)
--   thor_app              runtime read of tenant routing
--   thor_authorizer       authorizer's read-only subdomain -> routing lookup
--   thor_master_seed      write access to the specific master/auth tables seeded today
--
-- The Lambda applies the Master migrations before running this script, so the tables normally
-- exist by now; the table/schema grants are still guarded so role creation succeeds even if a
-- migration is ever missing (the grants then land on the next invocation). Per-tenant
-- tenant_<id>_rw/_ro roles are created later by the provisioning workflow, not here.

-- ── thor_provisioner ── DDL identity (create-tenant-database step only).
--   CREATEDB + CREATEROLE      -> create each tenant database and its _rw/_ro roles
--   rds_iam WITH ADMIN OPTION  -> authenticate via IAM token, and re-grant rds_iam to tenants
--   No auth/master grants: it never reads or writes the metadata tables.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'thor_provisioner') THEN
    CREATE ROLE thor_provisioner WITH LOGIN CREATEDB CREATEROLE;
  ELSE
    ALTER ROLE thor_provisioner WITH LOGIN CREATEDB CREATEROLE;
  END IF;
END
$$;

GRANT rds_iam TO thor_provisioner WITH ADMIN OPTION;

-- ── thor_metadata_writer ── writes tenant metadata (seed + finalize-routing steps).
--   rds_iam + write access to the two auth tables only. No CREATEDB/CREATEROLE.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'thor_metadata_writer') THEN
    CREATE ROLE thor_metadata_writer WITH LOGIN;
  ELSE
    ALTER ROLE thor_metadata_writer WITH LOGIN;
  END IF;
END
$$;

GRANT rds_iam TO thor_metadata_writer;

DO $$
BEGIN
  IF to_regclass('auth.tenant') IS NOT NULL AND to_regclass('auth.tenant_routing') IS NOT NULL THEN
    GRANT USAGE ON SCHEMA auth TO thor_metadata_writer;
    GRANT SELECT, INSERT, UPDATE ON auth.tenant, auth.tenant_routing TO thor_metadata_writer;
  END IF;
  -- Read-only on master for FK/lookup checks when inserting a tenant (tier/isolation/status).
  IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'master') THEN
    GRANT USAGE ON SCHEMA master TO thor_metadata_writer;
    GRANT SELECT ON ALL TABLES IN SCHEMA master TO thor_metadata_writer;
  END IF;
END
$$;

-- ── thor_app ── runtime read identity (services resolve tenant routing). IAM, read-only.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'thor_app') THEN
    CREATE ROLE thor_app WITH LOGIN;
  ELSE
    ALTER ROLE thor_app WITH LOGIN;
  END IF;
END
$$;

GRANT rds_iam TO thor_app;

DO $$
BEGIN
  IF to_regclass('auth.tenant') IS NOT NULL AND to_regclass('auth.tenant_routing') IS NOT NULL THEN
    GRANT USAGE ON SCHEMA auth TO thor_app;
    GRANT SELECT ON auth.tenant, auth.tenant_routing TO thor_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'master') THEN
    GRANT USAGE ON SCHEMA master TO thor_app;
    GRANT SELECT ON ALL TABLES IN SCHEMA master TO thor_app;
  END IF;
END
$$;

-- ── thor_authorizer ── the authorizer's subdomain -> routing lookup (in-VPC, IAM via the proxy).
--   rds_iam (no password), read-only, and only the two tables the authorizer's join touches.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'thor_authorizer') THEN
    CREATE ROLE thor_authorizer WITH LOGIN;
  ELSE
    ALTER ROLE thor_authorizer WITH LOGIN;
  END IF;
END
$$;

GRANT rds_iam TO thor_authorizer;

DO $$
BEGIN
  IF to_regclass('auth.tenant') IS NOT NULL AND to_regclass('auth.tenant_routing') IS NOT NULL THEN
    GRANT USAGE ON SCHEMA auth TO thor_authorizer;
    GRANT SELECT ON auth.tenant, auth.tenant_routing TO thor_authorizer;
  END IF;
END
$$;

-- ── thor_master_seed ── writes deployment seed data (Thor.MasterDbSeed Lambda).
--   rds_iam, write access scoped to the specific master/auth tables seeded today. No CREATEDB/CREATEROLE.
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'thor_master_seed') THEN
    CREATE ROLE thor_master_seed WITH LOGIN;
  ELSE
    ALTER ROLE thor_master_seed WITH LOGIN;
  END IF;
END
$$;

GRANT rds_iam TO thor_master_seed;

DO $$
BEGIN
  IF to_regclass('master.authentication_types') IS NOT NULL
     AND to_regclass('master.connector_config_fields') IS NOT NULL
     AND to_regclass('master.authentication_fields') IS NOT NULL
     AND to_regclass('master.connector_types') IS NOT NULL THEN
    GRANT USAGE ON SCHEMA master TO thor_master_seed;
    GRANT SELECT, INSERT, UPDATE ON
      master.authentication_types, master.connector_config_fields, master.authentication_fields,
      master.connector_types
      TO thor_master_seed;
  END IF;

  IF to_regclass('auth.api_scopes') IS NOT NULL THEN
    GRANT USAGE ON SCHEMA auth TO thor_master_seed;
    GRANT SELECT, INSERT, UPDATE ON auth.api_scopes TO thor_master_seed;
  END IF;
END
$$;
