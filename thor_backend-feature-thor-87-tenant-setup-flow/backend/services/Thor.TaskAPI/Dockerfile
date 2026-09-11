FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Thor.TaskApi.csproj ./
RUN dotnet restore Thor.TaskApi.csproj
COPY . .
RUN dotnet publish Thor.TaskApi.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Thor.TaskApi.dll"]
