# Build context is the repo root (not this directory), so the shared projects under
# backend/shared/ and the proto contract under proto/ that Thor.Api.csproj references
# can be copied in: docker build -f backend/services/Thor.API/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Project files (plus the proto the Grpc.Tools codegen is wired to) first, so the
# NuGet restore layer caches independently of source edits.
COPY global.json ./
COPY proto proto
COPY backend/services/Thor.API/Thor.Api.csproj backend/services/Thor.API/
COPY backend/shared/Thor.Auth/Thor.Auth.csproj backend/shared/Thor.Auth/
COPY backend/shared/Thor.Core/Thor.Core.csproj backend/shared/Thor.Core/
COPY backend/shared/Thor.DataConnectionManager/Thor.DataConnectionManager.csproj backend/shared/Thor.DataConnectionManager/
COPY backend/shared/Thor.DataLayer/Thor.DataLayer.csproj backend/shared/Thor.DataLayer/
RUN dotnet restore backend/services/Thor.API/Thor.Api.csproj

COPY backend/shared backend/shared
COPY backend/services/Thor.API backend/services/Thor.API
RUN dotnet publish backend/services/Thor.API/Thor.Api.csproj --no-restore -c Release -o /app -p:OpenApiGenerateDocumentsOnBuild=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl isn't in this base image by default; needed for the ECS healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app

# Self-signed cert for NLB TLS re-encryption; unused unless TLS_CERT_PFX_PATH is set.
RUN apt-get update && apt-get install -y --no-install-recommends openssl \
    && mkdir -p /app/certs \
    && openssl req -x509 -newkey rsa:2048 -nodes \
         -keyout /app/certs/server.key \
         -out /app/certs/server.crt \
         -days 825 \
         -subj "/CN=thor-api.internal" \
    && openssl pkcs12 -export \
         -out /app/certs/server.pfx \
         -inkey /app/certs/server.key \
         -in /app/certs/server.crt \
         -passout pass:thor-internal \
    && rm /app/certs/server.key /app/certs/server.crt \
    && apt-get purge -y --auto-remove openssl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./
EXPOSE 8080
EXPOSE 8443
ENTRYPOINT ["dotnet", "Thor.Api.dll"]
