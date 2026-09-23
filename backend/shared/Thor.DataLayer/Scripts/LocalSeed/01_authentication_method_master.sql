-- Flow 1a/2: Authentication method — Master DB half.
-- Run against the Master metadata database (THOR_MASTERDB_* in .env), schema "master".
--
-- Seeds authentication_types and their authentication_fields (the fields each type
-- requires, e.g. username/password). tenant.authentication_methods.type (seeded by
-- 02_authentication_method_tenant.sql) points at an authentication_types id here — the
-- `id` values below are the join key between this script and that one, keep them in
-- sync if you change either. authentication_fields.type_id has a real FK to
-- authentication_types (ON DELETE CASCADE), both within this same database.
--
-- Run 00_connector_types.sql first — authentication_types.connector_type_id is a real FK
-- to master.connector_types.id (same database).
--
-- Run before 02_authentication_method_tenant.sql: that script references these ids and
-- will leave a dangling cross-database reference if this script hasn't run yet (there is
-- no physical FK between the two databases to enforce it).
--
-- This does NOT seed authentication_values (the actual credential values for a field) —
-- those are created via AWS Secrets Manager at runtime (see
-- docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md), not fabricated here.
--
-- Edit the VALUES rows below to match the connector types/fields you actually need;
-- these are placeholder examples. Safe to re-run (ON CONFLICT DO NOTHING).

INSERT INTO master.authentication_types (id, name, connector_type_id)
VALUES
    ('a1a1a1a1-0001-4000-8000-000000000001', 'Active Directory Service Account', 0),
    ('a1a1a1a1-0001-4000-8000-000000000002', 'Microsoft 365 App Registration', 1)
ON CONFLICT (id) DO NOTHING;

INSERT INTO master.authentication_fields (id, type_id, name, description)
VALUES
    ('b1b1b1b1-0001-4000-8000-000000000001', 'a1a1a1a1-0001-4000-8000-000000000001', 'username', 'Service account username (e.g. DOMAIN\svc-account).'),
    ('b1b1b1b1-0001-4000-8000-000000000002', 'a1a1a1a1-0001-4000-8000-000000000001', 'password', 'Service account password.'),
    ('b1b1b1b1-0001-4000-8000-000000000003', 'a1a1a1a1-0001-4000-8000-000000000002', 'tenant_id', 'Azure AD tenant (directory) ID.'),
    ('b1b1b1b1-0001-4000-8000-000000000004', 'a1a1a1a1-0001-4000-8000-000000000002', 'client_id', 'App registration (application) client ID.'),
    ('b1b1b1b1-0001-4000-8000-000000000005', 'a1a1a1a1-0001-4000-8000-000000000002', 'client_secret', 'App registration client secret.')
ON CONFLICT (id) DO NOTHING;
