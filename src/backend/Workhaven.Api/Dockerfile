FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
WORKDIR /source

COPY global.json Directory.Build.props .editorconfig ./
COPY src/backend/Workhaven.Api/Workhaven.Api.csproj src/backend/Workhaven.Api/
RUN dotnet restore src/backend/Workhaven.Api/Workhaven.Api.csproj

COPY src/backend/Workhaven.Api/ src/backend/Workhaven.Api/
RUN dotnet publish src/backend/Workhaven.Api/Workhaven.Api.csproj \
    --configuration Release --no-restore --output /app/publish -p:UseAppHost=false

FROM build AS migration-script
COPY .config/dotnet-tools.json .config/
RUN dotnet tool restore
# Generating SQL needs configuration, but never connects to a database.
RUN dotnet ef migrations script --idempotent --no-build --configuration Release \
    --project src/backend/Workhaven.Api --output /app/migrations.sql \
    -- --Database:Host=localhost --Database:Name=workhaven \
    --Database:Username=unused --Database:Password=unused

FROM postgres:18.6-trixie AS migrations
COPY --from=migration-script /app/migrations.sql /database/migrations.sql
COPY development/postgres/init-app-user.sh development/postgres/migrate.sh /database/
USER postgres
ENTRYPOINT ["sh", "/database/migrate.sh"]

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
