FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY examples/SharedServices/OrderService/ examples/SharedServices/OrderService/
RUN dotnet publish examples/SharedServices/OrderService \
    -c Release -o /app/publish -v q

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# curl is used by the compose healthcheck (aspnet runtime image ships without it).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:6201
EXPOSE 6201
ENTRYPOINT ["dotnet", "OrderService.dll"]
