# Connector auth secrets (JWT keypair + API-key pepper)

Three pieces of secret material back connector authentication (ADR §5.2/§8). Terraform creates
the secrets that hold them; **it never holds the values**, the same split already used for the
authorizer salt.

| Secret | Holds | Read by |
|--------|-------|---------|
| `thor-<env>-secret-connector-jwt` | JSON `{"private_key", "public_key"}` — the RS256 keypair | `thor-api` (private half), `task-api` (public half) |
| `thor-<env>-secret-api-key-pepper` | Random 32-byte base64 string | `thor-api` |
| `infra/envs/<env>/connector-jwt-public-key.pem` | Public half, **committed** | authorizer Lambda |

Only `thor-api` ever sees the private half, so a compromised `task-api` or authorizer can verify
connector tokens but never mint them.

## Why the public key is committed rather than referenced

ECS resolves a task definition's `secrets` at task start, so `thor-api` and `task-api` pull their
halves straight from Secrets Manager and nothing sensitive enters a task definition or state file.

Lambda has no such indirection — its environment variables are literals Terraform must materialize
at apply time. Sourcing the public key from the secret would mean reading it with
`aws_secretsmanager_secret_version`, which writes **the whole secret, private half included, into
tfstate in plaintext**. So the authorizer gets its key from a committed `.pem` instead. That is
safe because the value is public by design; it is the one place the two halves can drift apart, so
`scripts/seed-connector-secrets.sh` always writes both together.

## Seeding an environment

```
./scripts/seed-connector-secrets.sh <dev|qa|prod>
```

Run it from anywhere — it resolves paths from its own location. It needs `openssl`, `jq`, and an
AWS CLI already authenticated against that environment's account (dev and qa share one account;
prod is separate). On Windows, run it from Git Bash or WSL; `jq` is not installed by default
(`winget install jqlang.jq`).

The script refuses to run until the secrets exist, generates the keypair and pepper in a temp
directory it deletes on exit, writes both secrets, and drops the public `.pem` into the
environment directory. No private key is ever written outside that temp directory.

### Ordering on a brand-new environment

This module creates the secret the keypair lives in, so there is nothing to seed until after the
first apply:

1. `terragrunt apply` — creates both secrets, empty
2. `./scripts/seed-connector-secrets.sh <env>` — generates and seeds them, writes the `.pem`
3. Commit the `.pem`, `terragrunt apply` again — the authorizer picks up the public key

Between steps 1 and 3 the authorizer cannot initialize, which is where it already sits today. The
`connector_jwt_public_key` variable defaults to `""` purely to let step 1 run at all; it is not a
value to leave empty on purpose.

On an environment that already exists, only step 2 is new work — followed by a forced ECS
deployment, since ECS resolves `valueFrom` at task start and a running task keeps whatever it
started with.

## Rotation

Re-running the script mints a **new** keypair and pepper. This is not a no-op:

- Every connector JWT signed with the old private key stops verifying immediately.
- Every API key and refresh token hashed with the old pepper stops matching — those hashes cannot
  be recomputed, so **every connector must re-register**.

The script detects an existing `.pem` and warns before doing this. Rotating `dev` is cheap;
rotating `qa` or `prod` is a coordinated operation, not a maintenance task.

## IAM

Three grants make this work, all in `infra/src/modules/ecs/iam.tf`:

- **Execution roles** get `secretsmanager:GetSecretValue` on the bare secret ARNs. ECS resolves
  `valueFrom` with the *execution* role — the same grant on the task role leaves the container in
  `PENDING` with a `ResourceInitializationError`. The ARN must be bare: the `:<json-key>::` suffix
  a `valueFrom` accepts matches nothing as an IAM resource.
- **`thor-api`'s task role** gets `CreateSecret`/`GetSecretValue`/`PutSecretValue` on
  `secret:tenant/*`, for the per-tenant connector credentials `AuthenticationSecretWriter` stores.
- **`task-api`'s task role** gets `s3:PutObject`/`GetObject`/`AbortMultipartUpload` on the uploads
  bucket. It presigns with its own credentials, so a presigned PUT is only as permissive as this
  grant.

All three are additionally capped by the `thor-<env>-role-boundary` permissions boundary, which is
Console-created and not Terraform-managed. If the boundary omits S3 or Secrets Manager, these
policies attach and still deny at runtime.
