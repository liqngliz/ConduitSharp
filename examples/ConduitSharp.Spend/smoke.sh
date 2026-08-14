#!/usr/bin/env bash
# TokenFlow smoke test. Runs the image on a throwaway port against a stub upstream,
# so no Anthropic or ChatGPT credential is involved and no live gateway is disturbed.
#
#   ./smoke.sh                       build if needed, run every check
#   IMAGE=tokenflow:nc ./smoke.sh    reuse an image you already built
#   PORT=5091 MOCK_PORT=5092         override if those are taken
#
# Checks: /info, dashboard html + js bundle, one claude call, one codex call,
# wire log, spend JSONL, synthetic rows readable through /api/spend.
set -uo pipefail

cd "$(dirname "$0")/../.."

IMAGE=${IMAGE:-tokenflow:smoke}
PORT=${PORT:-5091}
MOCK_PORT=${MOCK_PORT:-5092}
NAME=tokenflow-smoke
DATA=$(mktemp -d)
TMP=$(mktemp -d)
FAILS=0
TODAY=$(date -u +%Y-%m-%d)
FAKEDAY=$(python3 -c "import datetime;print((datetime.date.today()-datetime.timedelta(days=1)).isoformat())")

ok()   { printf '  \033[32mok\033[0m    %s\n' "$1"; }
bad()  { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; FAILS=$((FAILS+1)); }
have() { if [ -n "${2:-}" ]; then ok "$1"; else bad "$1"; fi; }

cleanup() {
  docker rm -f "$NAME" >/dev/null 2>&1
  if [ -n "${MOCK_PID:-}" ]; then kill "$MOCK_PID" 2>/dev/null; wait "$MOCK_PID" 2>/dev/null; fi
  rm -rf "$DATA" "$TMP"
}
trap cleanup EXIT

# ── stub upstream ─────────────────────────────────────────────────────────────
# Answers in the two body shapes routes.json reads counts out of. Numbers are
# distinct per route and per token class so a mis-mapped field is visible.
cat > "$TMP/mock.py" <<'PY'
import json, sys
from http.server import BaseHTTPRequestHandler, HTTPServer

CLAUDE = {"id": "msg_smoke", "type": "message", "model": "test-model-claude",
          "usage": {"input_tokens": 1200, "output_tokens": 340,
                    "cache_creation_input_tokens": 56, "cache_read_input_tokens": 7800}}
# subtractInputFields takes cached_tokens off input, so in = 2100 - 1600 = 500.
CODEX = {"id": "resp_smoke", "model": "test-model-codex",
         "usage": {"input_tokens": 2100, "output_tokens": 410,
                   "input_tokens_details": {"cached_tokens": 1600, "cache_write_tokens": 90}}}

class H(BaseHTTPRequestHandler):
    def do_POST(self):
        self.rfile.read(int(self.headers.get("content-length", 0)))
        body = json.dumps(CODEX if "codex" in self.path else CLAUDE).encode()
        self.send_response(200)
        self.send_header("content-type", "application/json")
        self.send_header("content-length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
    def log_message(self, *a): pass

HTTPServer(("0.0.0.0", int(sys.argv[1])), H).serve_forever()
PY
python3 "$TMP/mock.py" "$MOCK_PORT" & MOCK_PID=$!

# Routes derived from the shipped file, so plugin config stays in sync. Only the two
# upstream addresses move.
sed -e "s#https://api.anthropic.com#http://host.docker.internal:$MOCK_PORT#" \
    -e "s#https://chatgpt.com#http://host.docker.internal:$MOCK_PORT#" \
    examples/ConduitSharp.Spend/Configuration/routes.json > "$TMP/routes.json"

# Always build. Skipping when the tag exists made the smoke pass against an image from a
# previous session, reporting green on code that was never compiled. Docker's layer cache
# makes an unchanged rebuild a few seconds.
docker build -q -f examples/ConduitSharp.Spend/Dockerfile -t "$IMAGE" . >/dev/null || exit 1

docker rm -f "$NAME" >/dev/null 2>&1
docker run -d --name "$NAME" -p "$PORT:5050" \
  --add-host=host.docker.internal:host-gateway \
  -v "$DATA:/data" -v "$TMP/routes.json:/app/Configuration/routes.json:ro" \
  "$IMAGE" >/dev/null || exit 1

B=http://localhost:$PORT
for _ in $(seq 60); do curl -sf "$B/info" >/dev/null 2>&1 && break; sleep 0.5; done

echo "TokenFlow smoke — image $IMAGE, port $PORT, data $DATA"

# ── 1. /info ──────────────────────────────────────────────────────────────────
INFO=$(curl -sf "$B/info")
have "/info responds"                "$INFO"
have "/info names the claude route"  "$(printf '%s' "$INFO" | grep -F '/llm/claude')"
have "/info names the codex route"   "$(printf '%s' "$INFO" | grep -F '/llm/codex')"

# ── 2. dashboard ──────────────────────────────────────────────────────────────
HTML=$(curl -sf "$B/")
have "/ serves the dashboard html"   "$(printf '%s' "$HTML" | grep -F '<title>TokenFlow</title>')"
JS=$(printf '%s' "$HTML" | grep -o 'src="[^"]*\.js"' | head -1 | sed 's/src="//;s/"//')
if [ -n "$JS" ] && [ "$(curl -so /dev/null -w '%{http_code}' "$B$JS")" = 200 ] &&
   [ "$(curl -sf "$B$JS" | wc -c)" -gt 10000 ]; then
  ok "react bundle $JS served"
else
  bad "react bundle served (got '$JS')"
fi

# ── 3. one call through each route ────────────────────────────────────────────
CL=$(curl -so /dev/null -w '%{http_code}' -X POST "$B/llm/claude/v1/messages" \
  -H 'content-type: application/json' -H 'x-api-key: smoke-key' \
  -d '{"model":"test-model-claude","max_tokens":16,"messages":[{"role":"user","content":"smoke claude prompt"}]}')
[ "$CL" = 200 ] && ok "claude route proxied (200)" || bad "claude route proxied (got $CL)"

CX=$(curl -so /dev/null -w '%{http_code}' -X POST "$B/llm/codex/backend-api/codex/responses" \
  -H 'content-type: application/json' -H 'authorization: Bearer smoke-token' \
  -d '{"model":"test-model-codex","client_metadata":{"thread_id":"smoke-thread"},"input":[{"role":"user","content":[{"type":"input_text","text":"smoke codex prompt"}]}]}')
[ "$CX" = 200 ] && ok "codex route proxied (200)" || bad "codex route proxied (got $CX)"

sleep 2  # spend rows and the wire log are written off a background channel

# ── 4. logs on disk ───────────────────────────────────────────────────────────
[ -s "$DATA/conduit-wire.jsonl" ] && ok "wire log written" || bad "wire log written"
[ -s "$DATA/spend-$TODAY.jsonl" ] && ok "spend-$TODAY.jsonl written" || bad "spend-$TODAY.jsonl written"
if docker logs "$NAME" 2>&1 | grep -qiE '\bfail(ed)?\b|\berror\b'; then
  bad "container log clean"
  docker logs "$NAME" 2>&1 | grep -iE '\bfail(ed)?\b|\berror\b' | head -3 | sed 's/^/        /'
else
  ok "container log clean"
fi

# Token classes survived the route's field mapping, both directions.
python3 - "$DATA/spend-$TODAY.jsonl" <<'PY' || FAILS=$((FAILS+1))
import json, sys
rows = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
want = {"claude": dict(inp=1200, out=340, cw=56, cr=7800),
        "codex":  dict(inp=500,  out=410, cw=90, cr=1600)}
for route, w in want.items():
    hit = [r for r in rows if r["route"] == route]
    if not hit:
        print(f"  \033[31mFAIL\033[0m  live {route} row present"); sys.exit(1)
    r = hit[-1]
    got = dict(inp=r["in"], out=r["out"], cw=r["cacheWrite"], cr=r["cacheRead"])
    if got != w:
        print(f"  \033[31mFAIL\033[0m  live {route} counts {got} != {w}"); sys.exit(1)
    print(f"  \033[32mok\033[0m    live {route} row: in={got['inp']} cw={got['cw']} "
          f"cr={got['cr']} out={got['out']} model={r['model']}")
PY

# Every spend row's trace id resolves in the wire log. That join is the only way to get from
# a cost row to the bodies it was counted from; ts+route are shared by concurrent calls.
python3 - "$DATA/spend-$TODAY.jsonl" "$DATA/conduit-wire.jsonl" <<'PY' || FAILS=$((FAILS+1))
import json, sys
rows  = [json.loads(l) for l in open(sys.argv[1]) if l.strip()]
wire  = {json.loads(l).get("traceId") for l in open(sys.argv[2]) if l.strip()}
blank = [r for r in rows if not r.get("trace")]
if blank:
    print(f"  \033[31mFAIL\033[0m  {len(blank)} spend row(s) with no trace id"); sys.exit(1)
missing = [r["trace"] for r in rows if r["trace"] not in wire]
if missing:
    print(f"  \033[31mFAIL\033[0m  trace {missing[0]} absent from the wire log"); sys.exit(1)
print(f"  \033[32mok\033[0m    {len(rows)} spend row(s) join to the wire log by trace")
PY

# ── 5. synthetic rows ─────────────────────────────────────────────────────────
# Written to yesterday's file so they never interleave with the live writer.
python3 - "$DATA/spend-$FAKEDAY.jsonl" "$FAKEDAY" <<'PY'
import json, sys
path, day = sys.argv[1], sys.argv[2]
rows = [
  dict(route="claude", turn=1, inp=1500, out=420,  cw=200, cr=0,    p="fake claude prompt one"),
  dict(route="claude", turn=2, inp=800,  out=260,  cw=0,   cr=1500, p="fake claude prompt two"),
  dict(route="codex",  turn=1, inp=2400, out=610,  cw=350, cr=0,    p="fake codex prompt one"),
  dict(route="codex",  turn=2, inp=900,  out=1100, cw=0,   cr=2400, p="fake codex prompt two"),
]
with open(path, "w") as f:
    for i, r in enumerate(rows):
        f.write(json.dumps({
            "ts": f"{day}T2{i}:05:00+00:00", "route": r["route"], "model": "test-model",
            "servedModel": "test-model", "caller": "smoke0000000000", "in": r["inp"],
            "out": r["out"], "cacheWrite": r["cw"], "cacheRead": r["cr"],
            "session": f"smoke-{r['route']}", "turn": r["turn"], "tools": 0, "ms": 42,
            "streamed": False, "prompt": r["p"], "sessionName": f"smoke {r['route']}",
        }) + "\n")
PY
ok "wrote 4 synthetic rows to spend-$FAKEDAY.jsonl"

# ── 6. synthetic rows reach the dashboard's data path ─────────────────────────
have "/api/spend lists $FAKEDAY" \
  "$(curl -sf "$B/api/spend" | grep -F "\"$FAKEDAY\"")"

curl -sf "$B/api/spend/$FAKEDAY" > "$TMP/day.json"
python3 - "$TMP/day.json" <<'PY' || FAILS=$((FAILS+1))
import json, sys
rows = json.load(open(sys.argv[1]))
bad = []
if len(rows) != 4:
    bad.append(f"{len(rows)} rows, want 4")
if {r["model"] for r in rows} != {"test-model"}:
    bad.append(f"models {sorted({r['model'] for r in rows})}")
if not all(r.get("prompt", "").startswith("fake ") for r in rows):
    bad.append("prompt text missing")
if {r["route"] for r in rows} != {"claude", "codex"}:
    bad.append(f"routes {sorted({r['route'] for r in rows})}")
tot = tuple(sum(r[k] for r in rows) for k in ("in", "cacheWrite", "cacheRead", "out"))
if tot != (5600, 550, 3900, 2390):
    bad.append(f"totals {tot}")
if bad:
    print("  \033[31mFAIL\033[0m  synthetic rows through /api/spend: " + "; ".join(bad))
    sys.exit(1)
print("  \033[32mok\033[0m    /api/spend/<day> returns 4 rows, both routes, model=test-model")
print("  \033[32mok\033[0m    totals in=5600 cw=550 cr=3900 out=2390 intact through the API")
PY

echo
if [ "$FAILS" -eq 0 ]; then
  printf '\033[32mall checks passed\033[0m\n'
else
  printf '\033[31m%s check(s) failed\033[0m\n' "$FAILS"
fi
exit "$FAILS"
