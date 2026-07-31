FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .

# Build gateway
RUN dotnet publish examples/EmbeddedGatewayPrefixed/EmbeddedGatewayPrefixed.csproj \
    -c Release -o /app/publish -v q

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# curl is used by the compose healthcheck (aspnet runtime image ships without it).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Gateway-specific config: routes, scripts, specs
COPY examples/EmbeddedGatewayPrefixed/gateway/Configuration            ./Configuration
COPY examples/EmbeddedGatewayPrefixed/gateway/configuration-vm         ./configuration-vm
COPY examples/EmbeddedGatewayPrefixed/gateway/configuration-docker     ./configuration-docker
COPY examples/EmbeddedGatewayPrefixed/gateway/configuration-grafana    ./configuration-grafana
COPY examples/EmbeddedGatewayPrefixed/gateway/scripts                  ./scripts
COPY examples/EmbeddedGatewayPrefixed/gateway/specs                    ./specs

ENV ASPNETCORE_URLS=http://+:7050
EXPOSE 7050
ENTRYPOINT ["dotnet", "EmbeddedGatewayPrefixed.dll"]
