# Build context must be the repo root (this module references sibling projects under
# backend/workflows/Abstractions and backend/shared) — e.g.:
#   docker build -f backend/workflows/Thor.Workflows.Ingestion/Dockerfile -t thor-workflows-ingestion .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/workflows/Thor.Workflows.Ingestion/Thor.Workflows.Ingestion.csproj backend/workflows/Thor.Workflows.Ingestion/
COPY backend/workflows/Abstractions/Thor.Workflows.Abstractions/Thor.Workflows.Abstractions.csproj backend/workflows/Abstractions/Thor.Workflows.Abstractions/
COPY backend/shared/Thor.DataLayer/Thor.DataLayer.csproj backend/shared/Thor.DataLayer/
COPY backend/shared/Thor.DataConnectionManager/Thor.DataConnectionManager.csproj backend/shared/Thor.DataConnectionManager/
COPY backend/shared/Thor.S3/Thor.S3.csproj backend/shared/Thor.S3/
COPY backend/shared/Thor.Graph/Thor.Graph.csproj backend/shared/Thor.Graph/
RUN dotnet restore backend/workflows/Thor.Workflows.Ingestion/Thor.Workflows.Ingestion.csproj

COPY backend/workflows/Thor.Workflows.Ingestion/ backend/workflows/Thor.Workflows.Ingestion/
COPY backend/workflows/Abstractions/Thor.Workflows.Abstractions/ backend/workflows/Abstractions/Thor.Workflows.Abstractions/
COPY backend/shared/Thor.DataLayer/ backend/shared/Thor.DataLayer/
COPY backend/shared/Thor.DataConnectionManager/ backend/shared/Thor.DataConnectionManager/
COPY backend/shared/Thor.S3/ backend/shared/Thor.S3/
COPY backend/shared/Thor.Graph/ backend/shared/Thor.Graph/
RUN dotnet publish backend/workflows/Thor.Workflows.Ingestion/Thor.Workflows.Ingestion.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Thor.Workflows.Ingestion.dll"]
