-- Seeds the fields required by each authentication_type (master.authentication_fields).
INSERT INTO master.authentication_fields (id, type_id, name, display_name, description, input_type)
SELECT gen_random_uuid(), t.id, v.name, v.display_name, v.description, v.input_type
FROM (VALUES
    ('Local Username and Password', 'Username', 'Username', 'Enter Username', 'string'),
    ('Local Username and Password', 'Password', 'Password', 'Enter Password', 'password'),
    ('API Key', 'Username', 'Username', 'Enter Username', 'string'),
    ('API Key', 'API Key', 'API Key', 'Enter API Key', 'password'),
    ('LDAP Authentication', 'Domain', 'Domain FQDN', 'Enter Domain FQDN', 'string'),
    ('LDAP Authentication', 'Username', 'Username', 'Enter Username', 'string'),
    ('LDAP Authentication', 'Password', 'Password', 'Enter Password', 'password'),
    ('Windows Authentication', 'Domain', 'Domain FQDN', 'Enter Domain FQDN', 'string'),
    ('Windows Authentication', 'Username', 'Username', 'Enter Username', 'string'),
    ('Windows Authentication', 'Password', 'Password', 'Enter Password', 'password'),
    ('SSH Key', 'Username', 'Username', 'Enter Username', 'string'),
    ('SSH Key', 'Upload Private Key File', 'Upload Private Key File', 'Upload a Private Key File', 'file'),
    ('CyberArk', 'Vault Address', 'Vault Address', 'CyberArk Vault Address of the server', 'string'),
    ('CyberArk', 'Port', 'Port', 'CyberArk Vault Port of the server', 'number'),
    ('CyberArk', 'Upload Certificate', 'Upload Certificate', 'CyberArk Certification Path of the server', 'file'),
    ('CyberArk', 'Certificate Password', 'Certificate Password', 'CyberArk Certificate Password', 'password'),
    ('OAuth 2.0 Client Credentials', 'Client ID', 'Client ID', 'Enter Client ID', 'string'),
    ('OAuth 2.0 Client Credentials', 'Client Secret', 'Client Secret', 'Enter a Client Secret', 'radio,password'),
    ('OAuth 2.0 Client Credentials', 'Certificate', 'Certificate', 'Enter a Certificate', 'radio,file'),
    ('OAuth 2.0 Client Credentials', 'Certificate Password', 'Certificate Password', 'Enter Certificate Password', 'certificate-password')
) AS v(type_name, name, display_name, description, input_type)
JOIN master.authentication_types t ON t.name = v.type_name
WHERE NOT EXISTS (
    SELECT 1 FROM master.authentication_fields f
    WHERE f.type_id = t.id AND f.name = v.name
);
