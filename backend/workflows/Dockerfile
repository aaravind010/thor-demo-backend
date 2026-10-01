# syntax=docker/dockerfile:1

# One image definition for every workflow module. Pick which one with --build-arg WORKFLOW=<Name>,
# where <Name> is the suffix of the project: Thor.Workflows.<Name>.
#
# Build context must be the repo root — every workflow references backend/workflows/platform and
# backend/shared, and anything above the context is invisible to the build:
#   docker build -f backend/workflows/Dockerfile --build-arg WORKFLOW=Ingestion -t thor-workflows-ingestion .
#
# Replaces the per-workflow Dockerfile that hand-listed every csproj twice (once to restore, once to
# build) — 23 lines that had to be edited whenever a project reference changed, times one per
# workflow. The NuGet cache mount below recovers most of what that hand-maintained layer split was
# buying, without the maintenance.

ARG WORKFLOW=Ingestion

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG WORKFLOW
WORKDIR /src

# .dockerignore keeps bin/obj and local env files out.
COPY backend/ backend/

# The glob spans the workflow group folders (platform/, ingestion/, atre/, ...) so this does not need
# to know that Thor.Workflows.Ingestion lives under ingestion/. Project names are unique, so exactly
# one path matches; failing loudly here beats a confusing restore error.
#
# The cache mount survives across builds, so a source-only change re-resolves packages from the local
# cache rather than the network. It is a mount, not a layer, so nothing lands in the final image.
RUN --mount=type=cache,target=/root/.nuget/packages \
    set -eu; \
    PROJECT=$(ls -d backend/workflows/*/Thor.Workflows.${WORKFLOW}/Thor.Workflows.${WORKFLOW}.csproj 2>/dev/null | head -n1); \
    if [ -z "${PROJECT:-}" ]; then \
      echo "No project found for WORKFLOW=${WORKFLOW} (looked for backend/workflows/*/Thor.Workflows.${WORKFLOW}/Thor.Workflows.${WORKFLOW}.csproj)" >&2; \
      exit 1; \
    fi; \
    echo "Publishing ${PROJECT}"; \
    dotnet publish "${PROJECT}" -c Release -o /app

# aspnet, not runtime: Thor.Core (via Thor.DataConnectionManager) carries a FrameworkReference to
# Microsoft.AspNetCore.App, so the shared framework must be present even though this is a console host.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG WORKFLOW
WORKDIR /app
COPY --from=build /app ./

# Which step runs is THOR_STEP's job, read inside the process; this only selects the assembly.
ENV THOR_WORKFLOW_DLL=Thor.Workflows.${WORKFLOW}.dll

# Shell form because the assembly name is a build arg. `exec` is load-bearing: without it PID 1 stays
# /bin/sh, which does not forward SIGTERM, so every ECS task stop would wait out the 30s timeout and
# be SIGKILLed. The trailing "--" becomes $0 so any CMD args land in "$@" as usual.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$THOR_WORKFLOW_DLL\" \"$@\"", "--"]
