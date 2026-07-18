FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

# Framework-dependent, portable publish (no pinned RID). The SDK and aspnet base images are
# multi-arch manifests, so this produces an image that runs on the host's architecture —
# amd64 and arm64 alike. Pinning -r linux-x64 here would fail on arm64 hosts (e.g. Apple
# Silicon) with an "assembly architecture is not compatible" error at startup.
RUN dotnet publish src/ConduitSharp.Host \
    -c Release \
    -o /app/publish

# ──────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

# Default routes.json — mount your own with:
#   -v /path/to/routes.json:/app/Configuration/routes.json
# or override path with:
#   -e CONDUIT_ROUTES_PATH=/config/routes.json
COPY src/ConduitSharp.Host/Configuration/routes.json ./Configuration/routes.json

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ConduitSharp.Host.dll"]
