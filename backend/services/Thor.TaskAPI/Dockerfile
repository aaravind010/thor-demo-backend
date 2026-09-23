# Build context is the repo root (not this directory), so the shared projects under
# backend/shared/ that Thor.TaskApi.csproj references can be copied in:
# docker build -f backend/services/Thor.TaskAPI/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Project files first, so the NuGet restore layer caches independently of source edits.
COPY global.json ./
COPY backend/services/Thor.TaskAPI/Thor.TaskApi.csproj backend/services/Thor.TaskAPI/
COPY backend/shared/Thor.Auth/Thor.Auth.csproj backend/shared/Thor.Auth/
COPY backend/shared/Thor.Core/Thor.Core.csproj backend/shared/Thor.Core/
COPY backend/shared/Thor.DataConnectionManager/Thor.DataConnectionManager.csproj backend/shared/Thor.DataConnectionManager/
COPY backend/shared/Thor.DataLayer/Thor.DataLayer.csproj backend/shared/Thor.DataLayer/
RUN dotnet restore backend/services/Thor.TaskAPI/Thor.TaskApi.csproj

COPY backend/shared backend/shared
COPY backend/services/Thor.TaskAPI backend/services/Thor.TaskAPI
RUN dotnet publish backend/services/Thor.TaskAPI/Thor.TaskApi.csproj --no-restore -c Release -o /app -p:OpenApiGenerateDocumentsOnBuild=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl isn't in this base image by default; needed for the ECS healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app

# Self-signed cert — task-api always terminates TLS on Service Connect.
RUN apt-get update && apt-get install -y --no-install-recommends openssl \
    && mkdir -p /app/certs \
    && openssl req -x509 -newkey rsa:2048 -nodes \
         -keyout /app/certs/server.key \
         -out /app/certs/server.crt \
         -days 825 \
         -subj "/CN=task-api.internal" \
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
ENTRYPOINT ["dotnet", "Thor.TaskApi.dll"]
