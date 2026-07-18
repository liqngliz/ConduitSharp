FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .

# Build gateway
RUN dotnet publish src/ConduitSharp.Host \
    -c Release -o /app/publish -v q

# Build PowerShell plugin with the correct RID for the target container arch
RUN RID=$([ "$TARGETARCH" = "arm64" ] && echo "linux-arm64" || echo "linux-x64") && \
    dotnet publish examples/ConduitSharp.Plugin.PowerShell/src/ConduitSharp.Plugin.PowerShell \
    -c Release -r $RID --no-self-contained \
    -o /app/plugins/finance-margin-report -v q

# Build BodyCapture plugin
RUN dotnet publish examples/ConduitSharp.Plugin.BodyCapture/src/ConduitSharp.Plugin.BodyCapture \
    -c Release -o /app/plugins/body-capture -v q

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# curl is used by the compose healthcheck (aspnet runtime image ships without it).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY --from=build /app/plugins ./plugins

# Gateway-specific config: routes, scripts, specs
COPY examples/LegacyGateway/gateway/Configuration            ./Configuration
COPY examples/LegacyGateway/gateway/configuration-vm         ./configuration-vm
COPY examples/LegacyGateway/gateway/configuration-docker     ./configuration-docker
COPY examples/LegacyGateway/gateway/configuration-grafana    ./configuration-grafana
COPY examples/LegacyGateway/gateway/scripts                  ./scripts
COPY examples/LegacyGateway/gateway/specs                    ./specs

ENV ASPNETCORE_URLS=http://+:5050
EXPOSE 5050
ENTRYPOINT ["dotnet", "ConduitSharp.Host.dll"]
