FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Thor.TaskApi.csproj ./
RUN dotnet restore Thor.TaskApi.csproj
COPY . .
RUN dotnet publish Thor.TaskApi.csproj -c Release -o /app

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
