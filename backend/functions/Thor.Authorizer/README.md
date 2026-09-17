# Thor API Gateway Lambda Authorizer

Multi-tenant AWS API Gateway REQUEST authorizer. Validates Cognito JWTs and
tenant-issued API keys, resolves the tenant from the request's `Host` subdomain, and returns an
IAM policy with a flattened context (`tenant_id`, `caller_type`, `principal_id`, `scopes`) for the
downstream API. Route-level RBAC is enforced by the backend API, not here.

## Solution layout

- `src/Thor.Authorizer.Core` — all logic, no AWS/Lambda dependencies, fully unit-testable.
- `src/Thor.Authorizer.Function` — thin Lambda entry point + DI composition root.
- `test/Thor.Authorizer.Test` — xUnit + FluentAssertions + NSubstitute.

`Core`'s `DataAccess` layer is built against interfaces only (`ITenantRoutingRepository`,
`IApiKeyRepository`), currently wired to in-memory fakes in `CompositionRoot`. The real DB-backed
implementations are a separate project being built independently and will be swapped in via DI.

## Provisioning contracts (infra-side requirements this code assumes)

- **Credential format**: `Authorization: ApiKey {key_id}.{secret}` for API keys; a standard
  Cognito access token (JWT, optionally `Bearer `-prefixed) otherwise.
- **API key hashing**: `secret_hash` in the `tenant_api_key` table must be
  `PBKDF2-SHA256$<iterations>$<base64 hash>` (see `Pbkdf2ApiKeyHasher`). The salt is fetched from AWS Secrets Manager (see
  `SecretsManagerSaltProvider`, cached 5 min) via the `THOR_AUTHORIZER_SALT_SECRET_ID`
  environment variable, whose `SecretString` must be the base64-encoded salt bytes. The Lambda
  execution role needs `secretsmanager:GetSecretValue` on that secret — infra, not code. A
  missing/unreachable salt fails closed (Deny) for the API-key path only; the JWT path is
  unaffected.
- **Gateway Response mapping**: this Lambda can only return a bare IAM policy Deny (API Gateway
  defaults that to a 403). Shaping it into a 401 + JSON body requires a Gateway Response mapping
  configured in API Gateway — infra, not code.
