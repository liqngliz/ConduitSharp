#!/usr/bin/env bash
# Slow-reader comparison: 200 clients x 1 MB response, each draining at 64 KB/s.
# Usage: slowtest.sh <conduit|apisix>
set -uo pipefail
cd "$(dirname "$0")"
SC="./slowclient.py"

writes() { docker exec "$1" sh -c 't=0; for p in $(ls /proc 2>/dev/null | grep -E "^[0-9]+$"); do w=$(awk "/^wchar/{print \$2}" /proc/$p/io 2>/dev/null); t=$((t+${w:-0})); done; echo $t' 2>/dev/null || echo 0; }
rss() { docker stats --no-stream --format '{{.MemUsage}}' "$1" 2>/dev/null | awk '{print $1}'; }

if [ "$1" = "conduit" ]; then
    SVC=gateway; PORT=8080
    SCENARIO=scenario-a GW_MEM=512m docker compose up -d --build --force-recreate gateway upstream >/dev/null 2>&1
else
    SVC=apisix; PORT=9080
    mkdir -p apisix/.runtime && cp apisix/config-default.yaml apisix/.runtime/config.yaml
    APISIX_ROUTES=apisix GW_MEM=512m docker compose up -d --force-recreate upstream apisix >/dev/null 2>&1
fi
for _ in $(seq 1 60); do curl -fsS "http://127.0.0.1:$PORT/big1m" >/dev/null 2>&1 && break; sleep 1; done

CID=$(docker compose ps -q "$SVC")
[ -z "$CID" ] && { echo "$SVC not running"; exit 1; }
B=$(writes "$CID")
python3 "$SC" 127.0.0.1 "$PORT" /big1m 200 &
P=$!
sleep 8;  echo "  RSS t+8s : $(rss "$CID")"
sleep 6;  echo "  RSS t+14s: $(rss "$CID")"
wait $P
A=$(writes "$CID")
python3 -c "print(f'  container wrote {($A-$B)/1048576:,.0f} MiB while serving 200 MiB to slow readers')"
docker compose stop "$SVC" >/dev/null 2>&1
