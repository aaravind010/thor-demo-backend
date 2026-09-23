-- Seeds the connector-agnostic authentication schemes (master.authentication_types).
INSERT INTO master.authentication_types (id, name, connector_type)
SELECT gen_random_uuid(), v.name, 'generic'
FROM (VALUES
    ('API Key'),
    ('OAuth 2.0 Client Credentials'),
    ('LDAP Authentication'),
    ('Local Username and Password'),
    ('SSH Key'),
    ('Windows Authentication'),
    ('CyberArk')
) AS v(name)
WHERE NOT EXISTS (
    SELECT 1 FROM master.authentication_types t WHERE t.name = v.name
);
