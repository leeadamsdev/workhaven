FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
WORKDIR /source

COPY global.json Directory.Build.props .editorconfig ./
COPY src/backend/Workhaven.Api/Workhaven.Api.csproj src/backend/Workhaven.Api/
RUN dotnet restore src/backend/Workhaven.Api/Workhaven.Api.csproj

COPY src/backend/Workhaven.Api/ src/backend/Workhaven.Api/
RUN dotnet publish src/backend/Workhaven.Api/Workhaven.Api.csproj \
    --configuration Release --no-restore --output /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS runtime

# Compose uses curl to probe the API's health endpoint.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Workhaven.Api.dll"]
