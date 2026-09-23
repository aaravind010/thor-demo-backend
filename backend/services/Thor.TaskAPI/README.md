# Thor Task API

API interface to handle everything related to scan server, its configuration, scheduling scan and get tasks for scan servers.

Not internet-facing: clients reach it through [Thor.API](../Thor.API/README.md), which proxies
`/task-api/*` here and mints the connector tokens this service verifies.

## API Versioning

Versioning is handled by `Asp.Versioning.Mvc`, configured in `Program.cs`.

- **Scheme:** URL segment (`/v{version}/...`), e.g. `/v1/tasks`, `/v2/tasks`.
- **Default version:** `1.0` — assumed when no version segment is present.
- **Supported versions:** `1.0` (`Controllers/V1`), `2.0` (`Controllers/V2`).
- Not all endpoints are versioned — e.g. `uploads` (`Controllers/V1/UploadsController.cs`) has no
  version segment and is served regardless of API version.

### Response headers

- `api-version` — the version that actually served the request (e.g. `1.0`).
- `api-supported-versions` / `api-deprecated-versions` — emitted automatically
  (`ReportApiVersions = true`) listing all versions the matched endpoint supports/deprecates.

### Adding a new version

1. Add a `Controllers/V{n}` folder with a controller decorated with `[ApiVersion("{n}.0")]`
   and `[Route("v{version:apiVersion}/...")]`.
2. Deprecate old versions via `[ApiVersion("1.0", Deprecated = true)]` rather than removing them.

## Configuration

Every value below is required — the service fails closed without it (ADR §5/§6). Locally they
come from `.env` (loaded by `DotNetEnv` in `Program.cs`); see `.env.example`. In deployed
environments Terraform injects them, and **nothing is set by hand on a task definition** — see
`infra/src/main.tf` (`service_app_environment` / `service_app_secrets`).

| Variable | Deployed source |
|----------|-----------------|
| `THOR_MASTERDB_HOST` / `_DATABASE` / `_USER` / `_REGION` / `_PORT` | `local.masterdb_environment` — host is the RDS Proxy endpoint, auth is RDS IAM (no password) |
| `THOR_UPLOADS_BUCKET` | `module.uploads` output |
| `THOR_TASKAPI_JWT_PUBLIC_KEY` | ECS `secrets` → `thor-<env>-secret-connector-jwt`, `public_key` field |
| `TLS_CERT_PFX_PATH` / `_PASSWORD` | `infra/src/modules/ecs/services.tf`; the cert itself is baked into the image by the Dockerfile |

The JWT key is the **public** half only. Thor.Api holds the private half and is the only service
that can mint connector tokens; this service can verify them and never forge them (ADR §5.2).

### How a missing value fails

Worth knowing because the two failure modes look nothing alike:

- `THOR_UPLOADS_BUCKET` is read into `UploadOptions` eagerly, so the host dies at startup with
  `BucketName is required` and the task crash-loops. Loud and obvious.
- `THOR_TASKAPI_JWT_PUBLIC_KEY` is resolved lazily by the DI container, so the service **starts
  cleanly and passes its `/health` probe**, then throws on the first authenticated request. ECS
  reports the service healthy while every real call returns 500.

The connector JWT keypair and the API-key pepper are seeded out-of-band — see
`infra/bootstrap/connector-secrets.md`.

## Deployment

Built and deployed by `.github/workflows/deploy.yml`, which triggers on changes under
`backend/services/Thor.TaskAPI/**` or `backend/shared/**`.

- Push to `dev` — builds from source and deploys, then records the image digest in
  `deploy/task-api/image.json`.
- Push to `qa` / `main` — promotes the digest already in that manifest; no rebuild.
- Manual deploy/rollback — run the `Deploy` workflow with `service: task-api` and,
  optionally, an explicit `image_digest`.

### A config change needs a deploy, not just an apply

The ECS service sets `lifecycle { ignore_changes = [task_definition, ...] }`, because CI owns which
revision is live. A Terraform apply that changes this service's configuration therefore **registers
a new task definition revision and stops** — the running service stays on the revision it was
already on, with the old configuration, and nothing restarts.

The deploy workflow is what moves it: it reads the family's latest ACTIVE revision (the one the
apply registered), patches only the image, and updates the service. Three consequences:

- **Apply before deploying.** A deploy that runs first carries the *old* revision's configuration
  forward and skips straight past the apply's revision.
- **Seed secrets before deploying.** ECS resolves `secrets` at task start. A secret that exists but
  has no value yet fails that call and the task never starts — `ResourceInitializationError`,
  before any application log line.
- **Re-seeding is not live.** `scripts/seed-connector-secrets.sh` writes a new keypair, but a
  running task keeps the public half it resolved at start, so this service keeps verifying against
  the old key until it is redeployed. The `.pem` the script writes to
  `infra/envs/<env>/connector-jwt-public-key.pem` is not this service's copy — it is the
  authorizer's, a literal Lambda env var that only moves on a commit plus apply. Seed, commit the
  `.pem`, apply, then deploy; thor-api, task-api and the authorizer must all end up on the same
  keypair or connector tokens stop verifying somewhere.

A deploy of any one service also syncs its two siblings, so one deploy rolls thor-api, task-api and
intelligence-engine onto their newest revisions together.
