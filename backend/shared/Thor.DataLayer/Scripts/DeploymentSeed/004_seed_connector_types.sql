-- Seeds the canonical connector-type lookup (master.connector_types). Ids are fixed/manually
-- assigned (see ConnectorType.cs) so they stay stable references from tenant-DB rows.
INSERT INTO master.connector_types (id, name)
VALUES
    (1, 'Active Directory'),
    (2, 'Microsoft SQL'),
    (3, 'Windows Server'),
    (4, 'Unix Server'),
    (5, 'LDAP'),
    (6, 'Entra ID (Azure AD)'),
    (7, 'CyberArk'),
    (8, 'Generic Storage Device'),
    (9, 'BigID'),
    (10, 'Oracle'),
    (11, 'Sybase'),
    (12, 'MySQL'),
    (13, 'MongoDB'),
    (14, 'Cassandra'),
    (15, 'Redis'),
    (16, 'Service Now'),
    (17, 'HR Feed'),
    (18, 'Application Feed'),
    (19, 'Ownership Feed'),
    (20, 'Control Exceptions Feed'),
    (21, 'Targets Feed'),
    (22, 'Server to Application Feed'),
    (23, 'AWS'),
    (24, 'Azure'),
    (25, 'SailPoint')
ON CONFLICT (id) DO NOTHING;
