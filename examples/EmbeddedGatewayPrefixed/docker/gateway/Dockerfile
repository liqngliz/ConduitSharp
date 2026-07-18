FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .

# Build gateway
RUN dotnet publish examples/EmbeddedGateway/EmbeddedGateway.csproj \
    -c Release -o /app/publish -v q

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# curl is used by the compose healthcheck (aspnet runtime image ships without it).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Gateway-specific config: routes, scripts, specs
COPY examples/EmbeddedGateway/gateway/Configuration            ./Configuration
COPY examples/EmbeddedGateway/gateway/configuration-vm         ./configuration-vm
COPY examples/EmbeddedGateway/gateway/configuration-docker     ./configuration-docker
COPY examples/EmbeddedGateway/gateway/configuration-grafana    ./configuration-grafana
COPY examples/EmbeddedGateway/gateway/scripts                  ./scripts
COPY examples/EmbeddedGateway/gateway/specs                    ./specs

ENV ASPNETCORE_URLS=http://+:7050
EXPOSE 7050
ENTRYPOINT ["dotnet", "EmbeddedGateway.dll"]
