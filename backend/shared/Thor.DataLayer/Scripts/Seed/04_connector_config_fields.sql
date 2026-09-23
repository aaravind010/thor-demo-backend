-- Flow 3: Connector config fields — Master DB.
-- Run against the Master metadata database (THOR_MASTERDB_* in .env), schema "master".
--
-- Seeds connector_config_fields — the non-authentication, connector-specific settings
-- (e.g. a search base or page size) that a tenant's ScanConfig supplies a value for via
-- tenant.scan_connector_config_values (see Thor.API/Services/ScanConfigService.cs). This
-- table only defines the fields themselves; actual per-ScanConfig values are supplied by
-- the tenant at scan-config creation time, not seeded here.
--
-- Run 00_connector_types.sql first — connector_config_fields.connector_type_id is a real
-- FK to master.connector_types.id (same database, ON DELETE CASCADE).
--
-- `value` is an optional default surfaced to the caller when configuring a scan; leave it
-- NULL for fields with no sensible default (e.g. a tenant-specific search base).
--
-- Edit the VALUES rows below to match the connector fields you actually need; these are
-- placeholder examples. Safe to re-run (ON CONFLICT DO NOTHING).

INSERT INTO master.connector_config_fields (id, connector_type_id, field_name, value, display_name, description, input_type, required)
VALUES
    ('c1c1c1c1-0001-4000-8000-000000000001', 0, 'search_base', NULL, 'Search Base DN', 'LDAP search base distinguished name to scope directory queries (e.g. DC=corp,DC=example,DC=com).', 'text', true),
    ('c1c1c1c1-0001-4000-8000-000000000002', 0, 'page_size', '500', 'Page Size', 'Number of LDAP results to fetch per page.', 'number', false),
    ('c1c1c1c1-0001-4000-8000-000000000003', 1, 'base_url', 'https://graph.microsoft.com', 'Graph API Base URL', 'Microsoft Graph API base URL for this tenant''s cloud environment (e.g. Commercial vs. GCC High).', 'text', false),
    ('c1c1c1c1-0001-4000-8000-000000000004', 1, 'page_size', '999', 'Page Size', 'Number of Microsoft Graph API results to fetch per page.', 'number', false)
ON CONFLICT (id) DO NOTHING;
