-- Seeds the grantable API scopes tenant API keys can carry (auth.api_scopes).
INSERT INTO auth.api_scopes (scope_text)
SELECT 'connector'
WHERE NOT EXISTS (SELECT 1 FROM auth.api_scopes WHERE scope_text = 'connector');
