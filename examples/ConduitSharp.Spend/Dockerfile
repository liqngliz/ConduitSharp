# Build context is the repo root: this project references src/ and plugins/ directly.
#   docker build -f examples/ConduitSharp.Spend/Dockerfile -t conduit-spend .
#
# Published by the docker-spend job in .github/workflows/release.yml as
# ghcr.io/<owner>/conduit-spend, multi-arch, so users run it without cloning.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

# Framework-dependent, portable publish with no pinned RID, so one build runs on amd64 and
# arm64 alike. Pinning -r linux-x64 fails at startup on Apple Silicon.
RUN dotnet publish examples/ConduitSharp.Spend -c Release -o /app/publish

# ──────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# One routes file, written for the container: the local route uses host.docker.internal because
# 127.0.0.1 inside a container is the container, and the wire log writes under /data so both
# outputs land in the one mounted volume.
COPY examples/ConduitSharp.Spend/Configuration/routes.json ./Configuration/routes.json

ENV ASPNETCORE_URLS=http://+:4000
ENV CONDUIT_SPEND_DATA=/data

# Routes are baked in so the image runs with no arguments. Override either by mounting a file
# over ./Configuration/routes.json, or by pointing Gateway__RoutesPath at one you mount elsewhere.
ENV Gateway__RoutesPath=Configuration/routes.json
VOLUME /data
EXPOSE 4000

# Resolves host.docker.internal on plain Linux Docker before starting, so the local-model route
# works without --add-host. No-op on Docker Desktop, which already provides the name.
COPY examples/ConduitSharp.Spend/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
