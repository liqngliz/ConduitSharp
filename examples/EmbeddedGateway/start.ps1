#!/usr/bin/env pwsh
# Cross-platform launcher — works on Windows (pwsh), macOS, and Linux.
# Starts the gateway and all upstream services; services log to ./logs/ via Serilog.
#
# Usage:
#   pwsh start.ps1                # build then start everything (native dotnet)
#   pwsh start.ps1 -Build         # build only
#   pwsh start.ps1 -Stop          # kill all managed processes
#   pwsh start.ps1 -DockerUp      # build images and start full Docker stack
#   pwsh start.ps1 -DockerDown    # stop Docker stack

param(
    [switch]$Stop,
    [switch]$Build,
    [switch]$DockerUp,
    [switch]$DockerDown
)

$ErrorActionPreference = 'Stop'
$Root       = $PSScriptRoot
$LogDir     = Join-Path $Root "logs"
$BinDir     = Join-Path $Root "bin"
$PidFile    = Join-Path $Root ".pids"
$GatewayDir = [IO.Path]::GetFullPath((Join-Path $Root "EmbeddedGateway.csproj"))
$ComposeFile = Join-Path $Root "docker-compose.yml"

function Write-Step($m) { Write-Host "▶ $m" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host "✓ $m" -ForegroundColor Green }

# ---------------------------------------------------------------------------
# DockerDown
# ---------------------------------------------------------------------------
if ($DockerDown) {
    Write-Step "Stopping Docker stack..."
    docker compose -f $ComposeFile down
    Write-Ok "Docker stack stopped."
    exit 0
}

# ---------------------------------------------------------------------------
# DockerUp
# ---------------------------------------------------------------------------
if ($DockerUp) {
    Write-Step "Starting dockerized EmbeddedGateway + Aspire Dashboard..."
    docker compose -f $ComposeFile up --build -d

    $ApiKey = "demo-api-key-conduitsharp-example"
    $JWT = pwsh -NonInteractive -NoProfile -File "$Root/generate-token.ps1" 2>$null
    if (-not $JWT) { $JWT = "(python3 or pwsh required to generate token)" }

    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Yellow
    Write-Host "  ConduitSharp EmbeddedGateway — Docker" -ForegroundColor Yellow
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Gateway:          http://localhost:6050"
    Write-Host "  Aspire Dashboard: http://localhost:18888  (OTLP gRPC: 18889)"
    Write-Host ""
    Write-Host "  X-Api-Key: " -NoNewline; Write-Host $ApiKey -ForegroundColor Cyan
    Write-Host "  JWT:       " -NoNewline; Write-Host $JWT    -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  curl http://localhost:6050/health"
    Write-Host "  curl http://localhost:6050/api/inventory -H 'X-Api-Key: $ApiKey'"
    Write-Host "  curl http://localhost:6050/api/orders    -H `"Authorization: Bearer `$JWT`""
    Write-Host "  curl http://localhost:6050/erp/reports/summary -H `"Authorization: Bearer `$JWT`""
    Write-Host ""
    Write-Host "  Traces visible in Aspire Dashboard → Traces tab"
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

# ---------------------------------------------------------------------------
# Stop
# ---------------------------------------------------------------------------
if ($Stop) {
    if (Test-Path $PidFile) {
        Get-Content $PidFile | ForEach-Object {
            $id = [int]$_.Trim()
            $proc = Get-Process -Id $id -ErrorAction SilentlyContinue
            if ($proc) { $proc.Kill(); Write-Ok "Stopped PID $id ($($proc.ProcessName))" }
        }
        Remove-Item $PidFile -Force
    }
    # Fallback: kill by port in case PID file is stale
    foreach ($port in @(6050, 6060, 6101, 6102, 6201, 6301)) {
        if ($IsWindows) {
            $pids = netstat -ano 2>$null |
                Select-String ":$port\s" |
                ForEach-Object { ($_ -split '\s+')[-1] } |
                Where-Object { $_ -match '^\d+$' } |
                Select-Object -Unique
            foreach ($p in $pids) {
                $proc = Get-Process -Id ([int]$p) -ErrorAction SilentlyContinue
                if ($proc -and $proc.ProcessName -notmatch 'System') {
                    $proc.Kill()
                    Write-Ok "Killed PID $p on :$port"
                }
            }
        } else {
            $pids = lsof -ti ":$port" 2>$null
            if ($pids) {
                $pids | ForEach-Object { kill $_ 2>$null; Write-Ok "Killed PID $_ on :$port" }
            }
        }
    }
    # Final fallback: kill by process/dll name
    @('InventoryService', 'OrderService', 'GreeterService', 'ConduitSharp.Host') | ForEach-Object {
        Get-Process -Name $_ -ErrorAction SilentlyContinue | ForEach-Object {
            $_.Kill(); Write-Ok "Killed process $($_.ProcessName)"
        }
    }
    Write-Ok "All processes stopped."
    exit 0
}

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
$null = New-Item -ItemType Directory -Path $LogDir -Force
$null = New-Item -ItemType Directory -Path $BinDir -Force

Write-Step "Building InventoryService..."
dotnet publish "$Root/../SharedServices/InventoryService" -c Release -o "$BinDir/inventory-svc" -v q
Write-Ok "InventoryService → $BinDir/inventory-svc"

Write-Step "Building OrderService..."
dotnet publish "$Root/../SharedServices/OrderService" -c Release -o "$BinDir/order-svc" -v q
Write-Ok "OrderService → $BinDir/order-svc"

Write-Step "Building GreeterService (gRPC)..."
dotnet publish "$Root/../SharedServices/GreeterService" -c Release -o "$BinDir/greeter-svc" -v q
Write-Ok "GreeterService → $BinDir/greeter-svc"

if ($Build) { Write-Ok "Build complete."; exit 0 }

# ---------------------------------------------------------------------------
# Launch helper
# ---------------------------------------------------------------------------
$procs = @()

function Start-Svc($name, $dll, $port, $logFile) {
    Write-Step "Starting $name on :$port..."
    $env:ASPNETCORE_URLS = "http://localhost:$port"
    $env:LOG_FILE        = $logFile

    if ($IsWindows) {
        $p = Start-Process dotnet -ArgumentList $dll `
            -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput "$logFile.stdout" `
            -RedirectStandardError  "$logFile.stderr"
    } else {
        $p = Start-Process dotnet -ArgumentList $dll `
            -PassThru `
            -RedirectStandardOutput "/dev/null" `
            -RedirectStandardError  "/dev/null"
    }

    Remove-Item env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    Remove-Item env:LOG_FILE        -ErrorAction SilentlyContinue

    Write-Ok "$name PID $($p.Id) — log: $logFile"
    return $p
}

$invDll = Join-Path $BinDir "inventory-svc" "InventoryService.dll"
$ordDll = Join-Path $BinDir "order-svc"     "OrderService.dll"
$grtDll = Join-Path $BinDir "greeter-svc"   "GreeterService.dll"

$procs += Start-Svc "InventoryService-1" $invDll 6101 (Join-Path $LogDir "inventory-svc-1.log")
Start-Sleep -Seconds 1
$procs += Start-Svc "InventoryService-2" $invDll 6102 (Join-Path $LogDir "inventory-svc-2.log")
Start-Sleep -Seconds 1
$procs += Start-Svc "OrderService"       $ordDll 6201 (Join-Path $LogDir "order-svc.log")
Start-Sleep -Seconds 1
$procs += Start-Svc "GreeterService"     $grtDll 6301 (Join-Path $LogDir "greeter-svc.log")

Write-Step "Waiting 2s for services to initialise..."
Start-Sleep -Seconds 2

# ---------------------------------------------------------------------------
# Start gateway
# ---------------------------------------------------------------------------
Write-Step "Starting ConduitSharp gateway on :6050..."
$gatewayLog = Join-Path $LogDir "gateway.log"
$env:ASPNETCORE_URLS                            = "http://localhost:6050"
$env:Gateway__RoutesPath                        = [IO.Path]::GetFullPath((Join-Path $Root "gateway" "Configuration" "routes.json"))
$env:Gateway__BasePath                          = [IO.Path]::GetFullPath((Join-Path $Root "gateway"))
$env:GATEWAY_CONFIG_FILE                        = [IO.Path]::GetFullPath((Join-Path $Root "gateway" "configuration-vm" "appsettings.json"))

if ($IsWindows) {
    $gw = Start-Process dotnet -ArgumentList "run","--project",$GatewayDir,"-c","Release" `
        -PassThru -WindowStyle Hidden `
        -WorkingDirectory (Join-Path $Root "gateway") `
        -RedirectStandardOutput $gatewayLog `
        -RedirectStandardError  (Join-Path $LogDir "gateway.err.log")
} else {
    $gw = Start-Process dotnet -ArgumentList "run","--project",$GatewayDir,"-c","Release" `
        -PassThru `
        -WorkingDirectory (Join-Path $Root "gateway") `
        -RedirectStandardOutput $gatewayLog `
        -RedirectStandardError  (Join-Path $LogDir "gateway.err.log")
}

Remove-Item env:ASPNETCORE_URLS         -ErrorAction SilentlyContinue
Remove-Item env:Gateway__RoutesPath     -ErrorAction SilentlyContinue
Remove-Item env:Gateway__BasePath       -ErrorAction SilentlyContinue
Remove-Item env:GATEWAY_CONFIG_FILE     -ErrorAction SilentlyContinue

$procs += $gw
Write-Ok "Gateway PID $($gw.Id) — log: $gatewayLog"

# Save PIDs for -Stop
($procs | ForEach-Object { $_.Id }) | Set-Content $PidFile

$ApiKey = "demo-api-key-conduitsharp-example"
$JWT    = pwsh -NonInteractive -NoProfile -File "$Root/generate-token.ps1"

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Yellow
Write-Host "  ConduitSharp EmbeddedGateway example running" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Gateway:   http://localhost:6050"
Write-Host ""
Write-Host "  X-Api-Key: " -NoNewline; Write-Host $ApiKey -ForegroundColor Cyan
Write-Host "  JWT:       " -NoNewline; Write-Host $JWT    -ForegroundColor Cyan
Write-Host ""
Write-Host "  curl http://localhost:6050/health"
Write-Host "  curl http://localhost:6050/api/inventory -H 'X-Api-Key: $ApiKey'"
Write-Host "  curl http://localhost:6050/api/orders    -H `"Authorization: Bearer `$JWT`""
Write-Host "  curl http://localhost:6050/erp/reports/summary -H `"Authorization: Bearer `$JWT`""
Write-Host ""
Write-Host "  Logs:       $LogDir"
Write-Host "  OTel traces: $(Join-Path $LogDir 'otel-traces.jsonl')"
Write-Host "  Stop:       pwsh start.ps1 -Stop"
Write-Host ""
