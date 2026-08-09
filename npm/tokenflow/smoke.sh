#!/usr/bin/env bash
# npx smoke. Packs the working tree and runs the tarball through npx, so a break in the
# current commit fails here rather than after publish.
#
#   ./smoke.sh              pack + run + assert
#   CLEAN=1 ./smoke.sh      also assert no .NET exists before the run (container only)
#   PORT=5095 ./smoke.sh    override if taken
#
# CLEAN=1 is the negative control. Without it, a passing run proves the launcher works;
# it does not prove the launcher bootstrapped its own runtime rather than finding one on
# PATH. Every GitHub-hosted runner ships a .NET 10 SDK, so only the container leg can
# assert absence. macOS and Windows run without it.
set -uo pipefail

cd "$(dirname "$0")"

PORT=${PORT:-5095}
FAILS=0
DATA=$(mktemp -d)
RUNTIME="$HOME/.tokenflow/dotnet"
# tokenflow.js picks dotnet.exe on win32. Bash on a Windows runner sees the same path.
MUXER="$RUNTIME/dotnet"; [ "${OS:-}" = "Windows_NT" ] && MUXER="$RUNTIME/dotnet.exe"

ok()  { printf '  \033[32mok\033[0m    %s\n' "$1"; }
bad() { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; FAILS=$((FAILS+1)); }

cleanup() {
  # On Windows `kill` reaps the npx shim and leaves dotnet.exe holding the port and the
  # step's handles, which reads as a hung job rather than a failed one.
  if [ -n "${APP_PID:-}" ]; then
    if [ "${OS:-}" = "Windows_NT" ]; then
      taskkill //F //T //PID "$APP_PID" >/dev/null 2>&1
      taskkill //F //IM dotnet.exe >/dev/null 2>&1
    fi
    kill "$APP_PID" 2>/dev/null
  fi
  # The launcher log is the only diagnosis a CI failure gets; nobody can re-run the runner.
  [ "$FAILS" -gt 0 ] && [ -f "$DATA/out.log" ] && {
    echo; echo "--- launcher log, last 40 lines ---"; tail -40 "$DATA/out.log"; }
  rm -rf "$DATA" ./*.tgz
}
trap cleanup EXIT

echo "npx smoke: port $PORT, clean=${CLEAN:-0}"

# ── negative control ──────────────────────────────────────────────────────────
# Runs BEFORE the launcher, because the launcher creates ~/.tokenflow itself and
# would make an "is there a dotnet here" check pass for the wrong reason.
if [ "${CLEAN:-0}" = "1" ]; then
  [ -z "$(command -v dotnet)" ] && ok "no dotnet on PATH" || bad "dotnet on PATH at $(command -v dotnet)"
  [ -z "${DOTNET_ROOT:-}" ]     && ok "DOTNET_ROOT unset" || bad "DOTNET_ROOT=$DOTNET_ROOT"
  for d in /usr/share/dotnet /usr/lib/dotnet /usr/local/share/dotnet "$HOME/.dotnet" "$RUNTIME"; do
    [ -d "$d" ] && bad "dotnet dir exists: $d"
  done
  ok "no dotnet install dirs"
  dotnet --info >/dev/null 2>&1 && bad "dotnet --info succeeded" || ok "dotnet --info fails"
fi

# ── pack ──────────────────────────────────────────────────────────────────────
[ -f app/ConduitSharp.Spend.dll ] || { bad "app/ missing, run dotnet publish -o app first"; exit 1; }
TGZ=$(npm pack --silent 2>/dev/null | tail -1)
[ -f "$TGZ" ] && ok "packed $TGZ ($(du -h "$TGZ" | cut -f1))" || { bad "npm pack produced nothing"; exit 1; }

# ── run ───────────────────────────────────────────────────────────────────────
# A busy port fails as "dashboard not served" five checks later, against someone else's
# server. Catch it here instead.
curl -sf --connect-timeout 2 --max-time 3 -o /dev/null "http://localhost:$PORT/" \
  && { bad "port $PORT already serving something"; exit 1; }

CONDUIT_SPEND_DATA="$DATA" npx --yes --package="./$TGZ" -- \
  tokenflow --urls "http://localhost:$PORT" > "$DATA/out.log" 2>&1 &
APP_PID=$!

# Windows gets 420s: the npx shim, unpacking, and dotnet-install.ps1 together ran past the
# old 120s ceiling on windows-latest, so every check failed against a server still starting.
# --connect-timeout because a Windows runner can blackhole rather than refuse, and an
# untimed curl there spends the whole budget on connects.
WAIT=${WAIT:-180}; [ "${OS:-}" = "Windows_NT" ] && WAIT=${WAIT_WINDOWS:-420}
DEADLINE=$(( $(date +%s) + WAIT ))
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
  curl -sf --connect-timeout 2 --max-time 5 -o /dev/null "http://localhost:$PORT/" && break
  sleep 2
done

# ── assert ────────────────────────────────────────────────────────────────────
curl -s --max-time 5 "http://localhost:$PORT/" | grep -q '<title>TokenFlow</title>' \
  && ok "dashboard served" || bad "dashboard not served"

BUNDLE=$(curl -s --max-time 5 "http://localhost:$PORT/" | grep -o '/assets/[^"]*\.js' | head -1)
[ -n "$BUNDLE" ] && [ "$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$PORT$BUNDLE")" = 200 ] \
  && ok "react bundle $BUNDLE served" || bad "react bundle missing"

curl -s --max-time 5 "http://localhost:$PORT/info" | grep -q TokenFlow \
  && ok "/info responds" || bad "/info wrong or missing"

[ "$(curl -s --max-time 5 "http://localhost:$PORT/api/spend")" = "[]" ] \
  && ok "/api/spend returns []" || bad "/api/spend wrong"

# Proves the launcher downloaded a runtime instead of finding one. Under CLEAN this is
# the whole point: the dir was asserted absent above, so its existence now is the fetch.
[ -x "$MUXER" ] && ok "runtime bootstrapped into $RUNTIME" || bad "no runtime at $MUXER"

# Size first: an empty log passes a grep for error words, which is how a launcher that
# never produced output read as clean on windows-latest.
if [ ! -s "$DATA/out.log" ]; then
  bad "launcher log empty, nothing ran"
elif grep -qiE '\b(error|fail|exception)\b' "$DATA/out.log"; then
  bad "launcher log has errors"
else
  ok "launcher log clean"
fi

echo
[ "$FAILS" -eq 0 ] && printf '\033[32mall checks passed\033[0m\n' \
                   || printf '\033[31m%s check(s) failed\033[0m\n' "$FAILS"
exit "$FAILS"
