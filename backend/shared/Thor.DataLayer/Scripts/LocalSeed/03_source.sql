-- Flow 2: Source — Tenant DB.
-- Run against a tenant database (THOR_DATALAYER_* in .env, or whichever database a
-- tenant's routing resolves to), schema "tenant".
--
-- connector_type is an application-level reference to master.connector_types.id (see
-- 00_connector_types.sql: 0=ActiveDirectory, 1=Microsoft365) — no physical FK, since
-- Source lives in a separate per-tenant database from the Master DB (see ADR §6.2).
--
-- `config` is connector-specific JSON; '{}' is a placeholder. See
-- master.connector_config_fields for the fields a given connector_type expects.
--
-- Edit the VALUES rows below; these are placeholder examples. Safe to re-run
-- (ON CONFLICT DO NOTHING).

INSERT INTO tenant.source (id, connector_type, name, config, is_active, created_at, updated_at)
VALUES
    ('a3a3a3a3-0003-4000-8000-000000000001', 0, 'Corp Active Directory', '{}', true, now(), now()),
    ('a3a3a3a3-0003-4000-8000-000000000002', 1, 'Corp Microsoft 365', '{}', true, now(), now()),
    ('a3a3a3a3-0003-4000-8000-000000000003', 0, 'EU Active Directory', '{}', true, now(), now()),
    ('a3a3a3a3-0003-4000-8000-000000000004', 0, 'APAC Active Directory', '{}', true, now(), now()),
    ('a3a3a3a3-0003-4000-8000-000000000005', 1, 'Corp Microsoft 365 - EU Tenant', '{}', true, now(), now())
ON CONFLICT (id) DO NOTHING;
