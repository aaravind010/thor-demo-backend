-- Flow 1b/2: Authentication method — Tenant DB half.
-- Run against a tenant database (THOR_DATALAYER_* in .env, or whichever database a
-- tenant's routing resolves to), schema "tenant".
--
-- Seeds authentication_methods. `type` is a cross-database reference (see
-- Thor.DataLayer/Models/Tenants/AuthenticationMethod.cs) with no physical FK — it must
-- match an id already present in master.authentication_types. Run
-- 01_authentication_method_master.sql first.
--
-- This does NOT seed authentication_values (the actual credential values) — those are
-- created via AWS Secrets Manager at runtime (see
-- docs/architecture/ADR-CONNECTOR-CREDENTIAL-MANAGEMENT.md), not fabricated here.
--
-- Edit the VALUES rows below; these are placeholder examples. Safe to re-run
-- (ON CONFLICT DO NOTHING).

INSERT INTO tenant.authentication_methods (id, type, name, description)
VALUES
    ('a2a2a2a2-0002-4000-8000-000000000001', 'a1a1a1a1-0001-4000-8000-000000000001', 'Corp AD Service Account', 'Service account credentials for the corporate Active Directory domain.'),
    ('a2a2a2a2-0002-4000-8000-000000000002', 'a1a1a1a1-0001-4000-8000-000000000002', 'Corp M365 App Registration', 'App registration credentials for the corporate Microsoft 365 tenant.')
ON CONFLICT (id) DO NOTHING;
