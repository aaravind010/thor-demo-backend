-- Seeds the authentication schemes (master.authentication_types). Which connectors each scheme
-- may be used with is seeded separately in 005_seed_authentication_type_connector_types.sql.
INSERT INTO master.authentication_types (id, name)
SELECT gen_random_uuid(), v.name
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
