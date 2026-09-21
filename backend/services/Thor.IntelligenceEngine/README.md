# Thor Intelligence Engine

Python/LangChain service, served over gRPC to Thor.Api (see the shared contract at
`proto/thor/intelligence_engine/v1/intelligence_engine.proto`). Runs on ECS Fargate,
internal-only — see `docs/architecture/ADR.md` §7, §13.

This is a base setup: one generic `InvokeAgent` RPC dispatching by `agent_name` to a
small in-process registry (`src/thor_intelligence_engine/agents/registry.py`). The
default LLM provider is a fake chat model (no AWS credentials needed); the real
Bedrock provider is a documented but unused seam in `src/thor_intelligence_engine/llm.py`
until the Bedrock VPC PrivateLink endpoint exists (ADR §13, open question O-9).

## Folder structure

```
src/
  thor/                      generated gRPC/protobuf stubs (gitignored, see scripts/generate_grpc.py)
    intelligence_engine/v1/  mirrors the proto package thor.intelligence_engine.v1
  thor_intelligence_engine/  hand-written application code (the actual installable package)
    agents/                  agent registry + implementations
    servicers/               gRPC service implementation (binds to thor.*_pb2_grpc)
    server.py, main.py, config.py, llm.py
```

`src/thor` and `src/thor_intelligence_engine` are separate top-level packages, not nested.

`src/thor` is produced by `scripts/generate_grpc.py` (run explicitly, and as a Docker build
step — see below) from `proto/` (never hand-edited, excluded from ruff — see `pyproject.toml`)
and only exists to give the proto-generated code an import path matching its proto package name.

`src/thor_intelligence_engine` is the real
package (`pyproject.toml`'s `packages = ["src/thor_intelligence_engine"]`) — `server.py`
imports the generated stubs from `thor.intelligence_engine.v1` and wires them to the
hand-written servicer in `thor_intelligence_engine.servicers`.

## Local dev

```
uv sync # to install dependencies
uv run python scripts/generate_grpc.py   # regenerate gRPC stubs from proto/, gitignored
uv run pytest
uv run ruff check .
uv run python -m thor_intelligence_engine.main   # starts the server on GRPC_PORT (default 8443)
```

Copy `.env.example` to `.env` and adjust as needed. With `TLS_CERT_PATH`/`TLS_CERT_KEY_PATH`
unset, the server listens insecurely — fine for local dev, matches how Thor.Api only
enables TLS when `TLS_CERT_PFX_PATH` is set.

## Docker

Build context is the **repo root**, not this directory, since the image needs the
shared `proto/` folder:

```
docker build -f backend/services/Thor.IntelligenceEngine/Dockerfile .
```

## Health check

This service implements the standard `grpc.health.v1.Health` service (wired up in
`server.py`) rather than an HTTP endpoint, since it's pure gRPC (`h2`-only) and
won't answer a plain HTTP(S) GET. Infra's ECS container healthcheck
(`infra/src/modules/ecs/services.tf`) probes it accordingly, via
`python -m thor_intelligence_engine.healthcheck` (`healthcheck.py`) — a short-lived
CLI that calls the `Health/Check` RPC over an in-process gRPC client and exits
0/1, rather than exposing any additional port.

`intelligence-engine` always terminates TLS on ECS (services.tf's
`container_tls_enabled` is unconditionally `true` for it, unlike the NLB-fronted
`thor-api`), against the self-signed cert the Dockerfile bakes in
(`CN=intelligence-engine.internal`). Since the healthcheck always dials
`localhost`, it pins that exact cert as the trust root (`root_certificates=`)
and overrides the expected TLS server name (`grpc.ssl_target_name_override`) to
match the cert's CN — otherwise hostname verification would fail even though
it's genuinely talking to the right server.
