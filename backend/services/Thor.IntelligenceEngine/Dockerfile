# Build context is the repo root (not this directory), so the shared proto contract
# under proto/ can be copied in: docker build -f backend/services/Thor.IntelligenceEngine/Dockerfile .
FROM python:3.12-slim AS build
COPY --from=ghcr.io/astral-sh/uv:0.5.11 /uv /uvx /bin/

WORKDIR /repo
COPY proto proto
COPY backend/services/Thor.IntelligenceEngine backend/services/Thor.IntelligenceEngine
WORKDIR /repo/backend/services/Thor.IntelligenceEngine

# grpcio-tools is a dev dependency, so codegen needs the full environment; the venv is
# then re-synced without dev deps so only runtime dependencies ship in the image.
RUN uv sync --frozen \
    && uv run --no-sync python scripts/generate_grpc.py \
    && uv sync --frozen --no-dev

FROM python:3.12-slim AS runtime

WORKDIR /app

# Self-signed cert — intelligence-engine always terminates TLS on Service Connect.
RUN apt-get update && apt-get install -y --no-install-recommends openssl \
    && mkdir -p /app/certs \
    && openssl req -x509 -newkey rsa:2048 -nodes \
         -keyout /app/certs/server.key \
         -out /app/certs/server.crt \
         -days 825 \
         -subj "/CN=intelligence-engine.internal" \
    && apt-get purge -y --auto-remove openssl \
    && rm -rf /var/lib/apt/lists/*

# uv.lock records the project itself as `source = { editable = "." }`, so the venv resolves
# the package through an absolute path into src/ — keep the build stage's layout here or
# `python -m thor_intelligence_engine.main` can't import it.
WORKDIR /repo/backend/services/Thor.IntelligenceEngine
COPY --from=build /repo/backend/services/Thor.IntelligenceEngine/.venv ./.venv
COPY --from=build /repo/backend/services/Thor.IntelligenceEngine/src ./src
ENV PATH="/repo/backend/services/Thor.IntelligenceEngine/.venv/bin:$PATH"

EXPOSE 8443
ENTRYPOINT ["python", "-m", "thor_intelligence_engine.main"]
