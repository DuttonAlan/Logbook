# syntax=docker/dockerfile:1

# Builds two images from the same sources:
#   docker build --target runtime  -t logbook .            # the server with the web interface (default target)
#   docker build --target migrator -t logbook-migrator .   # applies the database migrations, then exits
#
# Logbook never migrates the database at startup, so run the migrator before every new version.

ARG DOTNET_VERSION=10.0
ARG NODE_VERSION=24

FROM node:${NODE_VERSION}-alpine AS web
WORKDIR /web

# Install in its own layer, so it is only repeated when the dependencies change.
COPY src/Logbook.Web/package.json src/Logbook.Web/package-lock.json ./
RUN npm ci

COPY src/Logbook.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /source

# Restore in its own layer, so it is only repeated when a project file changes.
COPY global.json Logbook.slnx .editorconfig ./
COPY src/Logbook.Api/Logbook.Api.csproj src/Logbook.Api/
COPY src/Logbook.Core/Logbook.Core.csproj src/Logbook.Core/
COPY src/Logbook.Infrastructure/Logbook.Infrastructure.csproj src/Logbook.Infrastructure/
RUN dotnet restore src/Logbook.Api/Logbook.Api.csproj

COPY src/Logbook.Api/ src/Logbook.Api/
COPY src/Logbook.Core/ src/Logbook.Core/
COPY src/Logbook.Infrastructure/ src/Logbook.Infrastructure/
# The web interface is served by the Api from wwwroot.
COPY --from=web /web/dist/ src/Logbook.Api/wwwroot/
RUN dotnet publish src/Logbook.Api/Logbook.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM build AS bundle
ARG DOTNET_EF_VERSION=10.0.13
RUN dotnet tool install --global dotnet-ef --version ${DOTNET_EF_VERSION}
ENV PATH="${PATH}:/root/.dotnet/tools"
# Creating the bundle builds the DbContext, which needs a connection string; it never connects. The real one is
# passed to the bundle when it runs.
RUN ConnectionStrings__Database="Host=localhost;Database=bundle" \
    dotnet ef migrations bundle \
        --project src/Logbook.Infrastructure \
        --startup-project src/Logbook.Api \
        --configuration Release \
        --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrator
WORKDIR /app
COPY --from=bundle /app/efbundle ./efbundle
USER $APP_UID
# Pass the connection string of the database owner like for the server, e.g. as the environment variable
# ConnectionStrings__Database=Host=…;Database=logbook;Username=…;Password=…
ENTRYPOINT ["./efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# libgssapi-krb5-2: Npgsql loads it at startup and prints an error without it.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Plain HTTP on 8080 for a TLS-terminating reverse proxy.
# To let Kestrel terminate TLS instead, set ASPNETCORE_URLS and Kestrel__Certificates__Default__*.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Logbook.Api.dll"]
