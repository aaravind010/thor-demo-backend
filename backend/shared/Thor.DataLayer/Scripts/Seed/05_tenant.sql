-- Flow 4: Tenant + tenant routing — Master DB.
-- Run against the Master metadata database (THOR_MASTERDB_* in .env), schema "auth".
--
-- Seeds auth.tenant and auth.tenant_routing — the tenant identity + routing metadata that
-- ITenantRoutingResolver (Thor.DataConnectionManager/Routing/TenantRoutingResolver.cs)
-- resolves at runtime to open a connection to the tenant's own database (see ADR §6.2/§6.3).
-- No dependency on the other seed scripts in this folder — safe to run in any order
-- relative to them.
--
-- tier_id/isolation_type_id/status_id have no defined lookup/enum anywhere in the codebase
-- yet (same kind of gap connector_type had before 00_connector_types.sql existed) — the
-- values below are placeholders with no confirmed meaning; edit once a real scheme exists.
--
-- database_name must match a database that actually exists on cluster_endpoint, with
-- TenantDbContext's schema applied to it (`dotnet ef database update --context
-- TenantDbContext`), for TenantConnectionManager to successfully open a connection at
-- request time (see Thor.DataConnectionManager/TenantConnectionManager.cs). The row below
-- mirrors the local dev tenant already referenced by THOR_DATALAYER_DATABASE in
-- Thor.DataLayer/.env.example (database "tenantone").
--
-- secret_arn is unused in Development — LocalTenantSecretResolver reads tenant DB
-- credentials from THOR_TENANTDB_USER/THOR_TENANTDB_PASSWORD instead (see
-- Thor.DataConnectionManager/Secrets/LocalTenantSecretResolver.cs), so any placeholder
-- value works locally. Non-Development environments need a real Secrets Manager ARN here.
--
-- Edit the VALUES rows below to match your environment; these are placeholder examples.
-- Safe to re-run (ON CONFLICT (tenant_id) DO NOTHING).

INSERT INTO auth.tenant (tenant_id, display_name, subdomain, tier_id, isolation_type_id, status_id, created_at, updated_at)
VALUES
    ('32a7f912-646b-41ca-881a-7393fcff4300', 'TenantOne', 'tenantone.com', 1, 1, 1, now(), now())
ON CONFLICT (tenant_id) DO NOTHING;

-- Note: "app_client_Id" (mixed case) must be double-quoted — TenantRouting.cs's
-- [Column("app_client_Id")] attribute created it as a case-sensitive quoted identifier,
-- inconsistent with every other column here (which are plain lowercase snake_case).
INSERT INTO auth.tenant_routing (tenant_id, cluster_endpoint, database_name, region, user_pool_id, "app_client_Id", secret_arn, created_at, updated_at)
VALUES
    ('32a7f912-646b-41ca-881a-7393fcff4300', 'localhost', 'tenantone', 'us-east-1', '32a7f912-646b-41ca-881a-7393fcff4300', '32a7f912-646b-41ca-881a-7393fcff4300', 'dummy_arn', now(), now())
ON CONFLICT (tenant_id) DO NOTHING;
