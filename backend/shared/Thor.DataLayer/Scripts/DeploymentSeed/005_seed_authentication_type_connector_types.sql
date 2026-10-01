-- Seeds which authentication schemes may be used with which connectors
-- (master.authentication_type_connector_types). Runs after 002 (authentication_types) and 004
-- (connector_types), both of which it references by name/id.
INSERT INTO master.authentication_type_connector_types (authentication_type_id, connector_type_id)
SELECT t.id, v.connector_type_id
FROM (VALUES
    ('Local Username and Password', 1::smallint),  -- Active Directory
    ('Local Username and Password', 2::smallint),  -- Microsoft SQL
    ('Local Username and Password', 3::smallint),  -- Windows Server
    ('Local Username and Password', 7::smallint),  -- CyberArk
    ('API Key', 25::smallint)                      -- SailPoint
) AS v(type_name, connector_type_id)
JOIN master.authentication_types t ON t.name = v.type_name
ON CONFLICT (authentication_type_id, connector_type_id) DO NOTHING;
