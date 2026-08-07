# Build context is the repo root: this project references src/ and plugins/ directly.
#   docker build -f examples/ConduitSharp.Spend/Dockerfile -t token-flow .
#
# Published by .github/workflows/tokenflow.yml as ghcr.io/<owner>/token-flow, multi-arch,
# so users run it without cloning.

# ──────────────────────────────────────────────────────────────────────────────
# Dashboard. wwwroot/ is gitignored build output, so the image builds it rather than
# expecting it in the checkout — that is what left the published image serving a 404.
# Pinned to BUILDPLATFORM: the output is static assets, identical on every arch, and an
# emulated arm64 npm build costs minutes for nothing.
FROM --platform=$BUILDPLATFORM node:22-alpine AS dashboard
WORKDIR /dash
COPY examples/ConduitSharp.Spend/dashboard/package.json examples/ConduitSharp.Spend/dashboard/package-lock.json ./
RUN npm ci
COPY examples/ConduitSharp.Spend/dashboard/ ./
RUN npm run build          # vite build.outDir is '../wwwroot', so this lands in /wwwroot

# ──────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

# Microsoft.NET.Sdk.Web treats wwwroot as publish content automatically, so dropping the
# built dashboard here is the whole wiring — no csproj change.
COPY --from=dashboard /wwwroot examples/ConduitSharp.Spend/wwwroot

# Framework-dependent, portable publish with no pinned RID, so one build runs on amd64 and
# arm64 alike. Pinning -r linux-x64 fails at startup on Apple Silicon.
RUN dotnet publish examples/ConduitSharp.Spend -c Release -o /app/publish

# ──────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5050
ENV CONDUIT_SPEND_DATA=/data

# routes.json ships host-shaped, since a `dotnet tool` install is the common case. One line has
# to differ here: 127.0.0.1 inside a container is the container, so the local-model route needs
# the Docker host alias. The wire log needs no edit, %CONDUIT_SPEND_DATA% resolves to /data above.
RUN sed -i 's#http://127\.0\.0\.1:1234#http://host.docker.internal:1234#' Configuration/routes.json \
 && grep -q host.docker.internal Configuration/routes.json

# Routes are baked in so the image runs with no arguments. Override either by mounting a file
# over ./Configuration/routes.json, or by pointing Gateway__RoutesPath at one you mount elsewhere.
ENV Gateway__RoutesPath=Configuration/routes.json
VOLUME /data
EXPOSE 5050

ENTRYPOINT ["dotnet", "ConduitSharp.Spend.dll"]
