## ConduitSharp — EmbeddedGateway example
## Linux / macOS launcher (GNU make required)
##
## Targets:
##   make build    — build services + PowerShell plugin (gRPC needs no plugin: YARP is the engine)
##   make run      — build and start everything
##   make stop     — kill all managed processes
##   make clean    — remove build artefacts and logs

ROOT    := $(shell pwd)
BIN     := $(ROOT)/bin
LOGS    := $(ROOT)/logs
GATEWAY := $(ROOT)/EmbeddedGateway.csproj
PID_INV1 := $(ROOT)/.pid-inv1
PID_INV2 := $(ROOT)/.pid-inv2
PID_ORD  := $(ROOT)/.pid-ord
PID_GRT  := $(ROOT)/.pid-grt
PID_GW   := $(ROOT)/.pid-gw

.PHONY: build run stop clean docker-up docker-down docker-grafana docker-grafana-down test-e2e

build:
	@mkdir -p $(BIN)/inventory-svc $(BIN)/order-svc $(LOGS)
	@echo "▶ Building InventoryService..."
	@dotnet publish ../SharedServices/InventoryService -c Release -o $(BIN)/inventory-svc -v q
	@echo "▶ Building OrderService..."
	@dotnet publish ../SharedServices/OrderService -c Release -o $(BIN)/order-svc -v q
	@echo "▶ Building GreeterService (gRPC)..."
	@dotnet publish ../SharedServices/GreeterService -c Release -o $(BIN)/greeter-svc -v q
	@echo "✓ Build complete"

run: build
	@echo "▶ Starting InventoryService-1 on :7101..."
	@ASPNETCORE_URLS=http://localhost:7101 LOG_FILE=$(LOGS)/inventory-svc-1.log \
		dotnet $(BIN)/inventory-svc/InventoryService.dll \
		>> $(LOGS)/inventory-svc-1.log 2>&1 & echo $$! > $(PID_INV1)
	@sleep 0.5
	@echo "▶ Starting InventoryService-2 on :7102..."
	@ASPNETCORE_URLS=http://localhost:7102 LOG_FILE=$(LOGS)/inventory-svc-2.log \
		dotnet $(BIN)/inventory-svc/InventoryService.dll \
		>> $(LOGS)/inventory-svc-2.log 2>&1 & echo $$! > $(PID_INV2)
	@sleep 0.5
	@echo "▶ Starting OrderService on :7201..."
	@ASPNETCORE_URLS=http://localhost:7201 LOG_FILE=$(LOGS)/order-svc.log \
		dotnet $(BIN)/order-svc/OrderService.dll \
		>> $(LOGS)/order-svc.log 2>&1 & echo $$! > $(PID_ORD)
	@sleep 0.5
	@echo "▶ Starting GreeterService (gRPC, h2c) on :7301..."
	@ASPNETCORE_URLS=http://localhost:7301 LOG_FILE=$(LOGS)/greeter-svc.log \
		dotnet $(BIN)/greeter-svc/GreeterService.dll \
		>> $(LOGS)/greeter-svc.log 2>&1 & echo $$! > $(PID_GRT)
	@echo "▶ Waiting 2s for services to initialise..."
	@sleep 2
	@echo "▶ Starting ConduitSharp gateway on :7050..."
	@( cd $(ROOT)/gateway && \
		ASPNETCORE_URLS=http://localhost:7050 \
		Gateway__RoutesPath=$(ROOT)/gateway/Configuration/routes.json \
		Gateway__BasePath=$(ROOT)/gateway \
		GATEWAY_CONFIG_FILE=$(ROOT)/gateway/configuration-vm/appsettings.json \
		exec dotnet run --project $(GATEWAY) -c Release \
		) >> $(LOGS)/gateway.log 2>&1 & echo $$! > $(PID_GW)
	@JWT=$$(bash $(ROOT)/generate-token.sh 2>/dev/null \
	|| pwsh -NonInteractive -NoProfile -File $(ROOT)/generate-token.ps1 2>/dev/null \
	|| echo "(python3 or pwsh required — run: bash generate-token.sh)"); \
	echo ""; \
	echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"; \
	echo " ConduitSharp EmbeddedGateway example running"; \
	echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"; \
	echo " Gateway:   http://localhost:7050  (gRPC/h2c: http://localhost:7060)"; \
	echo ""; \
	echo " X-Api-Key: demo-api-key-conduitsharp-example"; \
	echo " JWT:       $$JWT"; \
	echo ""; \
	echo " curl http://localhost:7050/health"; \
	echo " curl http://localhost:7050/api/inventory -H 'X-Api-Key: demo-api-key-conduitsharp-example'"; \
	echo " curl http://localhost:7050/api/orders -H \"Authorization: Bearer $$JWT\""; \
	echo " curl http://localhost:7050/finance/reports/margin -H \"Authorization: Bearer $$JWT\""; \
	echo ""; \
	echo " Logs: $(LOGS)/   Stop: make stop"; \
	echo ""

stop:
	@for pid_file in $(PID_INV1) $(PID_INV2) $(PID_ORD) $(PID_GRT) $(PID_GW); do \
		if [ -f $$pid_file ]; then \
			PID=$$(cat $$pid_file); \
			kill $$PID 2>/dev/null && echo "✓ Stopped PID $$PID" || true; \
			rm -f $$pid_file; \
		fi; \
	done
	@for port in 7050 7060 7101 7102 7201 7301; do \
		PID=$$(lsof -ti :$$port 2>/dev/null); \
		if [ -n "$$PID" ]; then \
			kill $$PID 2>/dev/null && echo "✓ Killed PID $$PID on :$$port" || true; \
		fi; \
	done
	@pkill -f "[I]nventoryService.dll" 2>/dev/null || true
	@pkill -f "[O]rderService.dll" 2>/dev/null || true
	@pkill -f "[G]reeterService.dll" 2>/dev/null || true
	@pkill -f "run.*EmbeddedGateway" 2>/dev/null || true
	@pkill -f "[E]mbeddedGateway.dll" 2>/dev/null || true
	@echo "✓ All processes stopped."

clean: stop
	@rm -rf $(BIN) $(LOGS)
	@echo "✓ Clean complete."

docker-up:
	@echo "▶ Starting dockerized EmbeddedGateway + Aspire Dashboard..."
	@docker compose -f $(ROOT)/docker-compose.yml up --build -d
	@JWT=$$(bash $(ROOT)/generate-token.sh 2>/dev/null \
	|| pwsh -NonInteractive -NoProfile -File $(ROOT)/generate-token.ps1 2>/dev/null \
	|| echo "(python3 or pwsh required to generate token)"); \
	echo ""; \
	echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"; \
	echo " ConduitSharp EmbeddedGateway — Docker"; \
	echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"; \
	echo " Gateway:          http://localhost:7050"; \
	echo " Aspire Dashboard: http://localhost:18888  (OTLP gRPC: 18889)"; \
	echo ""; \
	echo " X-Api-Key: demo-api-key-conduitsharp-example"; \
	echo " JWT:       $$JWT"; \
	echo ""; \
	echo " curl http://localhost:7050/health"; \
	echo " curl http://localhost:7050/api/inventory -H 'X-Api-Key: demo-api-key-conduitsharp-example'"; \
	echo " curl http://localhost:7050/api/orders -H \"Authorization: Bearer $$JWT\""; \
	echo " curl http://localhost:7050/finance/reports/margin -H \"Authorization: Bearer $$JWT\""; \
	echo ""; \
	echo " Traces visible in Aspire Dashboard → Traces tab"; \
	echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

docker-down:
	@docker compose -f $(ROOT)/docker-compose.yml down
	@echo "✓ Docker stack stopped."

docker-grafana:
	@echo "▶ Starting dockerized EmbeddedGateway + Grafana stack..."
	@docker compose -f $(ROOT)/docker-compose.grafana.yml up --build -d
	@JWT=$$(bash $(ROOT)/generate-token.sh 2>/dev/null \
	|| pwsh -NonInteractive -NoProfile -File $(ROOT)/generate-token.ps1 2>/dev/null \
	|| echo "(python3 or pwsh required to generate token)"); \
	echo ""; \
	echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"; \
	echo " ConduitSharp EmbeddedGateway — Grafana"; \
	echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"; \
	echo " Gateway:  http://localhost:7050"; \
	echo " Grafana:  http://localhost:3000  (no login required)"; \
	echo "           Dashboard → ConduitSharp Gateway"; \
	echo ""; \
	echo " X-Api-Key: demo-api-key-conduitsharp-example"; \
	echo " JWT:       $$JWT"; \
	echo ""; \
	echo " curl http://localhost:7050/health"; \
	echo " curl http://localhost:7050/api/inventory -H 'X-Api-Key: demo-api-key-conduitsharp-example'"; \
	echo " curl http://localhost:7050/api/orders -H \"Authorization: Bearer $$JWT\""; \
	echo " curl http://localhost:7050/finance/reports/margin -H \"Authorization: Bearer $$JWT\""; \
	echo ""; \
	echo " Signals: Traces → Tempo  |  Metrics → Prometheus  |  Logs → Loki"; \
	echo " Stop: make docker-grafana-down"; \
	echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

docker-grafana-down:
	@docker compose -f $(ROOT)/docker-compose.grafana.yml down -v
	@echo "✓ Grafana stack stopped."

test-e2e:
	@dotnet test $(ROOT)/../../tests/ConduitSharp.EmbeddedGateway.E2E.Tests --no-restore

# Full observability-pipeline E2E: docker compose (grafana stack) + assertions
# that traces/metrics/logs land in Tempo/Prometheus/Loki. Requires Docker.
test-e2e-grafana:
	@dotnet test $(ROOT)/../../tests/ConduitSharp.Grafana.E2E.Tests --no-restore
