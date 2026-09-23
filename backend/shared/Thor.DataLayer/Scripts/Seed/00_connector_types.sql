-- Flow 0: Connector types — Master DB.
-- Run against the Master metadata database (THOR_MASTERDB_* in .env), schema "master".
--
-- Seeds connector_types, the canonical id->name lookup for "connector type" everywhere
-- else in the codebase uses a smallint: authentication_types.connector_type_id and
-- connector_config_fields.connector_type_id (both real FKs to this table, same database),
-- plus every tenant-DB ingestion table (source.connector_type, account.connector_type,
-- etc.) and scan_connector_config_values.connector_type, which reference these ids at the
-- application level only (no physical FK — separate database, see ADR §6.2).
--
-- Run first: 01_authentication_method_master.sql references these ids.
--
-- Ids are fixed, not auto-generated — do not renumber existing rows once tenant-DB data
-- references them. Edit/add VALUES rows for additional connectors as needed. Safe to
-- re-run (ON CONFLICT DO NOTHING).

INSERT INTO master.connector_types (id, name)
VALUES
    (0, 'ActiveDirectory'),
    (1, 'Microsoft365')
ON CONFLICT (id) DO NOTHING;
