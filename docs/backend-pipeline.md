# How the backend deploy pipeline works

Builds, promotes, and deploys `thor-api`, `task-api`, and `intelligence-engine` to ECS. One entry point workflow decides *what* should happen based on the trigger; a second, reusable workflow does the actual work for whichever environment that turns out to be.

Source: [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) (entry point) → [`.github/workflows/_deploy-core.yml`](../.github/workflows/_deploy-core.yml) (does the work) / [`.github/workflows/deploy-preview.yml`](../.github/workflows/deploy-preview.yml) (PR-only check)

## The four things that can happen

| Trigger | Mode | What runs |
|---|---|---|
| Push to `dev` | `deploy` | Build from source, publish to JFrog, promote into dev's ECR, deploy to ECS |
| Push to `qa` | `promote` | Pull dev's already-built image by digest, run it as a candidate, gate on approval, deploy |
| Push to `main` | `promote` | Pull qa's already-built image by digest, deploy to prod |
| Pull request into `dev`/`qa`/`main` | `preview` | Compile + unit test only — no Docker, no AWS, nothing deployed |
| Manual (`workflow_dispatch`) | `deploy` or `promote` | Pick an environment/service directly, optionally pin an exact JFrog digest (rollback) |

Only services with actual code changes get their own deploy job — `dorny/paths-filter` diffs the push/PR against each service's own folder (plus `backend/shared/**`, since all three depend on it) or, on qa/main, against that service's manifest file.

## Flow 1 — push to dev

```
detect-changes -> context -> deploy-<service> -> _deploy-core.yml: dev job -> write-manifests
                                                   (build -> JFrog -> dev ECR -> ECS)
```

Every changed service builds from source, publishes to JFrog (the single source of truth for what got built), tags that same image into dev's own ECR, then deploys it. The digest JFrog assigns gets written to the `dev` key in `deploy/<service>/image.json` — that's the only thing qa reads to know what to promote.

## Flow 2 — push to qa

```
detect-changes (manifest filter) -> context -> _deploy-core.yml: qa-deploy job
                                                (pull dev's digest -> tag :candidate -> deploy -> integration test)
  on pass -> qa-promote (required reviewer on the "qa" Environment) -> write-manifests
  on fail -> auto-rollback to the last stable ECS revision, nothing promoted
```

qa never builds anything — it pulls the exact digest dev's manifest recorded, straight from JFrog, and deploys it as a `:candidate`-tagged image first. If the integration test step fails, ECS is rolled back automatically, no approval needed for that part. Only after the candidate passes does `qa-promote` — gated by a required reviewer — retag it as the verified image.

## Flow 3 — push to main (prod)

```
detect-changes (manifest filter) -> context -> _deploy-core.yml: prod job (required reviewer on "prod")
                                                (pull qa's digest -> prod ECR -> ECS) -> write-manifests
```

Same idea as qa, one hop further down the chain: prod pulls the digest qa's manifest recorded (not dev's). No integration test step here — qa already ran it.

## Flow 4 — pull request (any target branch)

```
detect-changes (source filter) -> context (mode = preview) -> deploy-preview.yml
                                                                (restore + build + unit tests only)
```

Confirms the code still compiles and unit tests pass. Nothing touches Docker or AWS — qa/main promote an already-built image anyway, so there's nothing to preview-build there either.

## The manifest system

`deploy/<service>/image.json` is the handoff between environments — one file per service, with a `dev`/`qa`/`prod` key each recording the JFrog digest, commit SHA, and timestamp of the last thing deployed to that environment:

```json
{
  "service": "thor-api",
  "dev":  { "digest": "", "commitId": "", "deployedAt": "" },
  "qa":   { "digest": "", "commitId": "", "deployedAt": "" },
  "prod": { "digest": "", "commitId": "", "deployedAt": "" }
}
```

`write-manifests` (in `deploy.yml`) writes all three services' manifests in one job, once per push — not once per service inside `_deploy-core.yml` — so concurrent service deploys never race each other writing the same-ish files. Written via the GitHub Contents API (`manifest-write` action), with a retry-on-conflict loop, so it works even without a full git checkout/push cycle.

## Composite actions this depends on

| Action | Used by | Does |
|---|---|---|
| [`ecr-login`](../.github/actions/ecr-login/action.yml) | every stage | Assumes the environment's AWS role via OIDC, logs Docker in to that account's ECR |
| [`jfrog-login`](../.github/actions/jfrog-login/action.yml) | dev, qa-deploy | Logs Docker in to JFrog's Docker registry |
| [`ecs-deploy`](../.github/actions/ecs-deploy/action.yml) | dev, qa-deploy, prod | Registers a new task-definition revision, updates the ECS service, polls until the new revision is primary and healthy — also redeploys the other two services if their image didn't change, so all three stay in sync |
| [`manifest-write`](../.github/actions/manifest-write/action.yml) | write-manifests | Writes the JSON manifest via the GitHub Contents API |

`ecs-deploy` targets ECS by the naming convention the Terraform `ecs` module already establishes: cluster `thor-<environment>`, service name = the service key (`thor-api`), task-definition family `thor-<environment>-<service>`.

## A note on this repo's folder names

The pipeline's service identifier is always the kebab-case ECS/ECR key — `thor-api`, `task-api`, `intelligence-engine`. That's what shows up in ECR repo names, ECS resource names, and every `deploy/<service>/` path. The actual .NET/Python source, though, lives under PascalCase folders (`backend/services/Thor.API`, `Thor.TaskAPI`, `Thor.IntelligenceEngine`) — so `_deploy-core.yml`/`deploy-preview.yml`'s `working-directory` and `deploy.yml`'s `paths-filter` globs map the service key to the real folder name explicitly. If a fourth service is ever added, that mapping expression needs a new branch too — it won't fall out automatically from the service key alone.

---

# Prerequisites

Everything below has to exist before a push actually deploys anything. Nothing here will silently "half work" — a missing piece fails loudly (wrong role, missing secret, `ClusterNotFoundException`, etc.), not silently.

## 1. AWS: the three deploy roles (shared with the infra pipeline)

`deploy-dev`, `deploy-qa`, `deploy-prod` — **these are the exact same roles `infra.yml` uses**, not separate backend-only roles. If the infra pipeline is already set up per [`infra_pipeline_setup_guide.md`](Infra_pipeline/infra_pipeline_setup_guide.md), this step is already done. If not, follow that guide's Steps 2–4 (OIDC provider + the three roles + the four `*_AWS_ROLE_ARN` repo variables) before continuing here — this doc won't repeat those instructions.

One addition specific to this pipeline: `deploy-qa`'s trust policy needs to trust **both** the `qa-candidate` and `qa` GitHub Environments (see Step 2 below) — not just `qa` — since qa's candidate stage and promote stage run under two different Environment contexts.

## 2. GitHub Environments

**Repo → Settings → Environments.** Four Environments, not three:

| Environment | Required reviewers? | Used by |
|---|---|---|
| `dev` | No | `_deploy-core.yml`'s `dev` job |
| `qa-candidate` | No | `_deploy-core.yml`'s `qa-deploy` job (build + integration test) |
| `qa` | **Yes** | `_deploy-core.yml`'s `qa-promote` job (the actual gate) |
| `prod` | **Yes** | `_deploy-core.yml`'s `prod` job |

`dev` and `qa` are shared with the infra pipeline (`infra.yml`) — if those already exist, only `qa-candidate` needs adding for this pipeline specifically.

## 3. JFrog credentials

Repo-level, **Settings → Secrets and variables → Actions**:

- `jf_api_thor` (**secret**) — JFrog access token
- `jf_usr_thor` (**variable**) — the username associated with that token
- `JFROG_HOSTNAME` (**variable**) — same one `infra.yml` already uses for the Terraform state backend
- `JFROG_DOCKER_REPOSITORY` (**variable**) — **not yet configured anywhere in this repo.** `_deploy-core.yml`'s `dev` job builds `<hostname>/<JFROG_DOCKER_REPOSITORY>/<service>:<sha>` — without this variable set, the dev build/publish step fails immediately. This needs to point at whatever Docker repo in JFrog Artifactory this org wants built images to land in.

## 4. ECR repositories must already exist

This pipeline pushes images, it doesn't create the repos to push them into — that's `infra.yml`/Terraform's job (the ECS module's ECR resources, one repo per service per environment: `thor-api-dev`, `task-api-dev`, etc.). Run the infra pipeline against an environment at least once before expecting a backend deploy there to succeed.

## 5. `backend/services/Thor.IntelligenceEngine` needs an actual Python project

As of writing, that folder only has a `README.md` — no `Dockerfile`, no `requirements.txt`. `intelligence-engine`'s deploy path (dev build, qa candidate, prod promote) will fail the moment it's triggered until that service is actually built out. Not a pipeline bug — just not ready yet.

## 6. `GITHUB_TOKEN` permissions

Already declared correctly in `deploy.yml` (`contents: write`) — `manifest-write` needs this to commit `deploy/<service>/image.json` via the Contents API. Nothing to configure here, just worth knowing it's required if this workflow ever gets copied elsewhere.

## Verifying it works

Push a small change to a file under `backend/services/Thor.API/**` on `dev`. You should see `detect-changes` → `context` → `deploy-thor-api` (which runs `_deploy-core.yml`'s `dev` job: build, JFrog publish, ECR promote, ECS deploy) → `write-manifests`, and `deploy/thor-api/image.json`'s `dev` key updated with a real digest afterward. Opening a PR against `dev` with the same change should instead run only `deploy-preview.yml` — compile and unit test, nothing deployed.

---

thor-backend · generated from [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml), [`_deploy-core.yml`](../.github/workflows/_deploy-core.yml), [`deploy-preview.yml`](../.github/workflows/deploy-preview.yml)
