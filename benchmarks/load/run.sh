#!/usr/bin/env bash
# Phase 2/3 load benchmarks (see docs/planning/BENCHMARKS_PLAN.md).
#
#   ./run.sh all                  # direct + scenario-a + scenario-b + flood
#   ./run.sh scenario-a           # one scenario
#   ./run.sh soak                 # 30-min soak (run explicitly, not part of 'all')
#
# Env knobs: DUR=30s CONNS=125 RATE=0 SOAK_DUR=30m PIN=0 RESULTS=results.md
#   RATE>0 adds a fixed-rate run per scenario — use that run's latency numbers
#   (open-loop max-QPS latency suffers coordinated omission).
#   PIN=1 applies the Linux core-pinning overlay.
set -euo pipefail
cd "$(dirname "$0")"

DUR="${DUR:-30s}"
CONNS="${CONNS:-125}"
RATE="${RATE:-0}"
SOAK_DUR="${SOAK_DUR:-30m}"
SOAK_RATE="${SOAK_RATE:-1000}"
RESULTS="${RESULTS:-results.md}"
JSONL="${JSONL:-runs.jsonl}"
PTF_DUR="${PTF_DUR:-15s}"  # per-step duration for push-to-failure ramps
GW_MEM="${GW_MEM:-1g}"     # per-gateway container memory ceiling; compose reads it
REPS="${REPS:-3}"          # measured reps per arm; the MEDIAN row is reported, spread noted
GATE_LOAD="${GATE_LOAD:-2.0}"  # refuse to bench while the Docker VM's 1-min load is above this
GATE_WAIT="${GATE_WAIT:-300}"  # seconds to wait for the VM to settle before aborting
export GW_MEM

COMPOSE=(docker compose)
[ "${PIN:-0}" = "1" ] && COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.pin.yml)

GATEWAY_URL="http://gateway:8080/bench"
DIRECT_URL="http://upstream:8081/bench"
OCELOT_URL="http://ocelot:8080/bench"
APISIX_URL="http://apisix:9080/bench"
ENVOY_URL="http://envoy:10080/bench"

# Bench-only HS256 token for scenario-b (secret is public, see scenario-b.json).
jwt() {
    python3 - <<'EOF'
import base64, hashlib, hmac, json, time
b64u = lambda b: base64.urlsafe_b64encode(b).rstrip(b"=").decode()
secret = base64.b64decode("Y29uZHVpdHNoYXJwLWxvYWQtYmVuY2gtc2lnbmluZy1rZXktMDEyMzQ1Njc4OQ==")
h = b64u(json.dumps({"alg": "HS256", "typ": "JWT"}).encode())
p = b64u(json.dumps({"sub": "bench", "exp": int(time.time()) + 86400}).encode())
print(f"{h}.{p}." + b64u(hmac.new(secret, f"{h}.{p}".encode(), hashlib.sha256).digest()))
EOF
}

wait_url() { # $1 = host-side URL, $2 = service name (for logs on failure)
    for _ in $(seq 1 60); do
        curl -fsS "$1" >/dev/null 2>&1 && return 0
        sleep 1
    done
    echo "$2 never became healthy" >&2
    "${COMPOSE[@]}" logs "$2" >&2
    exit 1
}

up() { # $1 = scenario json name, $2 = MaxTotalBufferedBodyBytes override
    # MAX_MEMORY_BUFFERED / MEMORY_THRESHOLD pass through from the caller's environment so a
    # scenario can size the tiers; compose supplies defaults for whatever is left unset.
    SCENARIO="$1" MAX_TOTAL_BUFFERED="${2:-134217728}" \
    MAX_MEMORY_BUFFERED="${MAX_MEMORY_BUFFERED:-67108864}" \
    MEMORY_THRESHOLD="${MEMORY_THRESHOLD:-1048576}" \
    SPILL_DIR="${SPILL_DIR:-/spill}" TMPFS_SIZE="${TMPFS_SIZE:-512m}" \
        "${COMPOSE[@]}" up -d --build --force-recreate gateway upstream >/dev/null 2>&1
    wait_url http://127.0.0.1:8080/healthz gateway
}

up_competitor() { # $1 = service, $2 = host-side readiness URL
    # OCELOT_RETRY passes through from the caller: Ocelot ships no retry, so the retry scenarios
    # hand-register one (ocelot/RetryQoSProvider.cs) rather than declare it unmeasurable.
    OCELOT_RETRY="${OCELOT_RETRY:-0}" "${COMPOSE[@]}" up -d --build --force-recreate upstream "$1"
    wait_url "$2" "$1"
}

# bombardier run → one markdown row appended to $RESULTS
bench() { # $1 = row label, rest = bombardier args
    # Gated like bench_shape: without this, sequential arms (compare, compare-hc) inherit the
    # previous arm's churn and run order decides the ranking — measured on this rig as a 2x
    # spread across three arms started 30s apart. readme-block.py publishes ratios from these
    # rows, so an un-gated run here becomes a published wrong number.
    rig_gate
    local label="$1 [c=$CONNS]"; shift
    echo ">> $label"
    "${COMPOSE[@]}" run --rm -T --quiet-pull load --print r --format json -l "$@" \
        | python3 row.py "$label" "$RESULTS" "$JSONL"
}

header() {
    # fresh machine-readable records per invocation; APPEND=1 accumulates (multi-pass runs)
    [ "${APPEND:-0}" = "1" ] || : > "$JSONL"
    cat >> "$RESULTS" <<EOF

## $(date -u +%FT%TZ) — DUR=$DUR CONNS=$CONNS RATE=$RATE PIN=${PIN:-0} host=$(uname -sm)

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
EOF
}

# Max container RSS while a flag file exists (crude sampler, 2s period).
sample_mem() { # $1 = service, $2 = flag file, $3 = out file
    local cid; cid=$("${COMPOSE[@]}" ps -q "$1")
    local max=0
    while [ -e "$2" ]; do
        local cur
        cur=$(docker stats --no-stream --format '{{.MemUsage}}' "$cid" 2>/dev/null | awk '{print $1}')
        # normalize MiB/GiB → MiB
        case "$cur" in
            *GiB) cur=$(python3 -c "print(float('${cur%GiB}')*1024)") ;;
            *MiB) cur=${cur%MiB} ;;
            *)    cur=0 ;;
        esac
        max=$(python3 -c "print(max(float('$max'), float('$cur')))")
        echo "$max" > "$3"
        sleep 2
    done
}

scenario_direct()   { up scenario-a; bench "direct-to-upstream (no gateway)" -c "$CONNS" -d "$DUR" "$DIRECT_URL"; }

scenario_a() {
    up scenario-a
    bench "scenario-a pure proxy (max QPS)" -c "$CONNS" -d "$DUR" "$GATEWAY_URL"
    [ "$RATE" -gt 0 ] && bench "scenario-a pure proxy (rate=$RATE)" -c "$CONNS" -d "$DUR" --rate "$RATE" "$GATEWAY_URL"
    return 0
}

scenario_b() {
    up scenario-b
    local token; token=$(jwt)
    bench "scenario-b jwt-auth + rate-limit (max QPS)" -c "$CONNS" -d "$DUR" -H "Authorization: Bearer $token" "$GATEWAY_URL"
    [ "$RATE" -gt 0 ] && bench "scenario-b jwt-auth + rate-limit (rate=$RATE)" -c "$CONNS" -d "$DUR" --rate "$RATE" -H "Authorization: Bearer $token" "$GATEWAY_URL"
    return 0
}

# Head-to-head competitors: same upstream, same load-gen, benched sequentially.
scenario_ocelot() {
    up_competitor ocelot "http://127.0.0.1:8083/bench"
    bench "ocelot pure proxy (max QPS)" -c "$CONNS" -d "$DUR" "$OCELOT_URL"
    [ "$RATE" -gt 0 ] && bench "ocelot pure proxy (rate=$RATE)" -c "$CONNS" -d "$DUR" --rate "$RATE" "$OCELOT_URL"
    "${COMPOSE[@]}" stop ocelot
}

scenario_apisix() {
    up_competitor apisix "http://127.0.0.1:9080/bench"
    bench "apisix pure proxy (max QPS)" -c "$CONNS" -d "$DUR" "$APISIX_URL"
    [ "$RATE" -gt 0 ] && bench "apisix pure proxy (rate=$RATE)" -c "$CONNS" -d "$DUR" --rate "$RATE" "$APISIX_URL"
    "${COMPOSE[@]}" stop apisix
}

scenario_envoy() {
    up_envoy envoy-stream
    bench "envoy pure proxy (max QPS)" -c "$CONNS" -d "$DUR" "$ENVOY_URL"
    [ "$RATE" -gt 0 ] && bench "envoy pure proxy (rate=$RATE)" -c "$CONNS" -d "$DUR" --rate "$RATE" "$ENVOY_URL"
    "${COMPOSE[@]}" stop envoy
}

# Phase 3: 6 MB uploads × 64 conns against a 32 MB global buffer budget.
# Expected: 503 load-shed for most requests, gateway memory stays bounded.
flood() {
    ensure_payload
    # PUT on a retry route — the only shape that still buffers after the streaming-by-default
    # rework (plain uploads stream and never touch the budget).
    up scenario-flood 33554432
    local flag mem; flag=$(mktemp); mem=$(mktemp)
    sample_mem gateway "$flag" "$mem" &
    bench "flood 6MB×64conn PUT+retry, 32MB budget (503=load-shed OK)" \
        -c 64 -d "$DUR" -m PUT -f /payload/6mb.bin \
        -H "Content-Type: application/octet-stream" "$GATEWAY_URL"
    rm -f "$flag"; wait || true
    echo "flood: max gateway RSS $(cat "$mem") MiB (budget: 32 MiB bodies + runtime)" | tee -a "$RESULTS"
}

ensure_payload() {
    [ -f payload/6mb.bin ] || dd if=/dev/urandom of=payload/6mb.bin bs=1m count=6 2>/dev/null \
        || dd if=/dev/urandom of=payload/6mb.bin bs=1M count=6
}

# Phase 3: ramp upload concurrency until the gateway stops coping, one gateway at a time.
#
# Where `flood` asserts a single fixed point, this walks the load up and records *where and how*
# each gateway breaks — the question that actually matters for a gateway. Peak QPS while healthy
# says nothing about the 3am behaviour; what you want to know is whether it sheds load or falls
# over. Every gateway gets the same mem_limit (GW_MEM), the same 6 MB bodies and the same load-gen.
#
# Gateways are grouped by what they actually DO with the body, not by name, because scoring a
# buffering gateway against a streaming one measures the shape and not the gateway:
#
#   push-to-failure (streaming) — conduitsharp + ocelot. Ocelot ships no retry (FileQoSOptions is
#     circuit-breaker + timeout; Ocelot.Provider.Polly adds only AddCircuitBreaker and AddTimeout),
#     so there is none to configure and it never buffers a body. ConduitSharp streams by default,
#     so these two match.
#
#   ptf-buffered — conduitsharp (retry route) + apisix on its stock config. APISIX ships with
#     request buffering on (its nginx.conf sets no proxy_request_buffering, so nginx's default
#     applies) and every body lands in client_body_temp — measured at ~48 MiB of writes per 48 MiB
#     uploaded. It can stream via the non-default proxy_request_buffering off snippet
#     (config-stream.yaml), but nginx documents that an unbuffered request cannot be retried, so
#     that config has no retry at all — same constraint ConduitSharp has, chosen globally instead
#     of per request. Stock APISIX buffers, so it belongs here, against ConduitSharp's buffered
#     path — not in the streaming ramp, where it would be the one doing extra work.
#
# A gateway that 503s is PASSING. A gateway that gets OOM-killed or stops answering is failing.
push_to_failure() { # $1 = label, $2 = url, $3 = compose service
    local label="$1" url="$2" svc="$3" cid
    for c in ${STEPS:-8 16 32 64 128 256}; do
        cid=$("${COMPOSE[@]}" ps -q "$svc")
        if [ -z "$cid" ] || [ "$(docker inspect -f '{{.State.Status}}' "$cid")" != "running" ]; then
            echo "**$label: container not running before c=$c — stopping ramp**" | tee -a "$RESULTS"
            return 0
        fi

        local flag mem; flag=$(mktemp); mem=$(mktemp)
        sample_mem "$svc" "$flag" "$mem" &
        # bombardier exits non-zero when every request errors, which is exactly the data point
        # we came for — never let it kill the ramp.
        CONNS="$c" bench "$label PUT" -c "$c" -d "$PTF_DUR" -m PUT -f "${PTF_PAYLOAD:-/payload/6mb.bin}" \
            -H "Content-Type: application/octet-stream" "$url" || true
        rm -f "$flag"; wait || true

        local status oom
        status=$(docker inspect -f '{{.State.Status}}' "$cid" 2>/dev/null || echo gone)
        oom=$(docker inspect -f '{{.State.OOMKilled}}' "$cid" 2>/dev/null || echo unknown)
        echo "$label c=$c: peak RSS $(cat "$mem") MiB (limit ${GW_MEM:-1g}), status=$status oom=$oom" | tee -a "$RESULTS"

        if [ "$oom" = "true" ] || [ "$status" != "running" ]; then
            echo "**$label DIED at c=$c — oom=$oom status=$status**" | tee -a "$RESULTS"
            return 0
        fi
    done
    echo "$label survived every step (${STEPS:-8 16 32 64 128 256})" | tee -a "$RESULTS"
}

# Pure proxy — the shape Ocelot and APISIX also run, so the three are comparable.
ptf_conduitsharp() {
    ensure_payload
    up scenario-a
    push_to_failure "push-to-failure conduitsharp (stream)" "$GATEWAY_URL" gateway
}

# ConduitSharp's buffered path: a retry route, the only shape that still buffers. Compared against
# APISIX, which buffers every body whether it retries or not — so both sides are writing the upload
# to disk before forwarding, and the ramp is a like-for-like question: who holds RSS under the cap,
# and who sheds cleanly instead of dying?
ptf_conduitsharp_buffered() {
    ensure_payload
    up scenario-flood "${MAX_TOTAL_BUFFERED:-536870912}"
    push_to_failure "push-to-failure conduitsharp (buffered: retry route)" "$GATEWAY_URL" gateway
}

ptf_ocelot() {
    ensure_payload
    up_competitor ocelot "http://127.0.0.1:8083/bench"
    push_to_failure "push-to-failure ocelot" "$OCELOT_URL" ocelot
    "${COMPOSE[@]}" stop ocelot || true
}

ptf_apisix() {
    ensure_payload
    up_competitor apisix "http://127.0.0.1:9080/bench"
    push_to_failure "push-to-failure apisix" "$APISIX_URL" apisix
    "${COMPOSE[@]}" stop apisix || true
}

ptf_envoy() {
    ensure_payload
    up_envoy envoy-retry
    push_to_failure "push-to-failure envoy" "$ENVOY_URL" envoy
    "${COMPOSE[@]}" stop envoy || true
}

# ═══════════════════════════════════════════════════════════════════════════════════════════
# Structured comparison, s1..s4. Each scenario fixes the SHAPE of the work first, then compares
# gateways doing that shape — because a buffering gateway benched against a streaming one measures
# the shape and nothing else (worth 4.6x on this rig, larger than any real difference between the
# products).
# ═══════════════════════════════════════════════════════════════════════════════════════════

ensure_payload_1mb() {
    [ -f payload/1mb.bin ] || dd if=/dev/urandom of=payload/1mb.bin bs=1m count=1 2>/dev/null \
        || dd if=/dev/urandom of=payload/1mb.bin bs=1M count=1
}

ensure_payload_24kb() {
    [ -f payload/24kb.bin ] || dd if=/dev/urandom of=payload/24kb.bin bs=1k count=24 2>/dev/null \
        || dd if=/dev/urandom of=payload/24kb.bin bs=1K count=24
}
ensure_payload_4kb() {
    [ -f payload/4kb.bin ] || dd if=/dev/urandom of=payload/4kb.bin bs=1k count=4 2>/dev/null \
        || dd if=/dev/urandom of=payload/4kb.bin bs=1K count=4
}

# Bytes written to storage by everything in a container — the ground truth for "did it allocate a
# buffer?". A gateway that streams writes ~nothing; one that buffers writes about a copy of every
# body. Absolute values are NOT comparable across gateways: .NET sends on sockets via send(), which
# wchar ignores, while nginx uses write()/writev(), which it counts. Read each gateway against its
# own streaming baseline, never against the other's number.
container_writes() { # $1 = service
    local cid; cid=$("${COMPOSE[@]}" ps -q "$1" 2>/dev/null)
    [ -z "$cid" ] && { echo 0; return; }
    docker exec "$cid" sh -c 't=0
        for p in $(ls /proc 2>/dev/null | grep -E "^[0-9]+$"); do
            w=$(awk "/^wchar/{print \$2}" /proc/$p/io 2>/dev/null); t=$((t + ${w:-0}))
        done
        echo $t' 2>/dev/null || echo 0
}

# One bench run, plus the buffering verdict for it.
#
# The verdict is a RATIO of bytes written to bytes uploaded, never an absolute: a busy run moves
# tens of GiB, so any fixed byte threshold calls everything a buffer. A gateway that buffers writes
# roughly one copy of every body (ratio ~1); a streaming one writes only incidentals (ratio ~0).
# Refuse to bench on a churning VM. Every wild number this rig has produced traced back to
# benching while the VM was still digesting container builds/teardowns — same config measured
# 3340 QPS on a settled VM and 106 QPS at load 9. Numbers taken above the gate are garbage;
# better to wait or abort loudly than to record them.
rig_gate() {
    local deadline=$((SECONDS + GATE_WAIT)) l
    while :; do
        l=$(docker run --rm alpine cat /proc/loadavg 2>/dev/null | awk '{print $1}')
        python3 -c "import sys; sys.exit(0 if float('${l:-99}') < $GATE_LOAD else 1)" && return 0
        if [ $SECONDS -ge $deadline ]; then
            echo "rig_gate: VM load $l still >= $GATE_LOAD after ${GATE_WAIT}s — refusing to bench" >&2
            exit 1
        fi
        echo "   rig_gate: VM load $l, waiting for < $GATE_LOAD ..."
        sleep 15
    done
}

bench_shape() { # $1 = label, $2 = service, $3 = method, $4 = url, $5 = payload, $6 = payload MiB
    rig_gate
    local before after r
    local jsons=()

    # Discarded warmup. .NET starts cold — tiered JIT, thread-pool growth, an empty ArrayPool — and
    # a first run measured 1256 QPS against 3407/3551 for the two after it. Measuring the cold run
    # understates the gateway by ~3x and reads as a loss. nginx warms far faster but gets the same
    # treatment, so the arms stay comparable.
    "${COMPOSE[@]}" run --rm -T --quiet-pull load --print r --format json -l \
        -c "$CONNS" -d "${WARMUP_DUR:-5s}" -m "$3" -f "$5" -H "Content-Type: application/octet-stream" "$4" \
        >/dev/null 2>&1 || true

    # REPS measured runs; the MEDIAN lands in the results table. One run of this rig is not a
    # number — identical config has produced 223 and 345 QPS back to back — so a quotable row
    # needs the median and the spread next to it.
    before=$(container_writes "$2")
    for r in $(seq 1 "$REPS"); do
        local json; json=$(mktemp); jsons+=("$json")
        echo ">> $1 [c=$CONNS] rep $r/$REPS"
        "${COMPOSE[@]}" run --rm -T --quiet-pull load --print r --format json -l \
            -c "$CONNS" -d "$DUR" -m "$3" -f "$5" -H "Content-Type: application/octet-stream" "$4" \
            > "$json" 2>/dev/null || true
    done
    after=$(container_writes "$2")

    # Median rep -> the table row (via row.py, same format as every other row).
    local median; median=$(python3 - "${jsons[@]}" <<'PY'
import json, sys
runs = []
for f in sys.argv[1:]:
    try:
        runs.append((json.load(open(f))["result"]["rps"]["mean"], f))
    except Exception:
        pass
runs.sort()
print(runs[len(runs)//2][1] if runs else "")
PY
)
    # Spread + buffering verdict, computed BEFORE the row is written so both travel into
    # runs.jsonl with it. These two numbers are the finding — a table without them cannot say who
    # buffered or whether the figure is quotable — so they belong in the machine-readable record,
    # not only in the markdown prose. metrics.py is a file rather than a heredoc because its
    # f-string format specs are brace-and-comma syntax the shell mangles when nested this deep.
    eval "$(python3 metrics.py "$1" "$before" "$after" "$RESULTS" "${6:-1}" "${jsons[@]}")"
    [ -n "$median" ] && python3 row.py "$1 [c=$CONNS med/$REPS]" "$RESULTS" "$JSONL" \
        --ratio "${RATIO:-0}" --spread "${SPREAD:-0}" --reps "$REPS" < "$median" || true
    printf '%s\n' "${VERDICT_LINES:-}"
    rm -f "${jsons[@]}"
}

up_apisix() { # $1 = routes yaml basename, $2 = config yaml basename
    APISIX_ROUTES="$1" APISIX_CONF="$2" TMPFS_SIZE="${TMPFS_SIZE:-512m}" \
        "${COMPOSE[@]}" up -d --force-recreate upstream apisix >/dev/null 2>&1
    wait_url "http://127.0.0.1:9080/bench" apisix
}

up_envoy() { # $1 = config yaml basename
    ENVOY_CONF="$1" TMPFS_SIZE="${TMPFS_SIZE:-512m}" \
        "${COMPOSE[@]}" up -d --force-recreate upstream envoy >/dev/null 2>&1
    wait_url "http://127.0.0.1:10080/bench" envoy
}

# ── s1: out of the box. Retries on idempotent paths, load is POST, stock configs all round. ───
# The lead scenario because it is how each gateway actually ships. The question is what each does
# with a POST it could never safely replay:
#   conduitsharp — buffering is method-aware, so a POST on a retry route still streams: no buffer.
#   apisix       — buffers it anyway. nginx cannot retry without buffering and does not special-case
#                  the method, so the cost is paid for a replay that will never happen.
#   ocelot       — retry built on its official Polly seam (AddPolly<TProvider>); the package ships
#                  breaker + timeout, so the policy is ours. Buffers every body to make the replay work.
s1_out_of_box() {
    ensure_payload_1mb
    echo "" | tee -a "$RESULTS"
    echo "== s1: out of the box — retries on idempotent paths, 1 MB POST, c=$CONNS ==" | tee -a "$RESULTS"

    up scenario-flood
    bench_shape "s1 conduitsharp (retry route, POST is method-aware)" gateway POST "$GATEWAY_URL" /payload/1mb.bin 1

    OCELOT_RETRY=1 up_competitor ocelot "http://127.0.0.1:8083/bench"
    bench_shape "s1 ocelot (retry via official Polly seam: buffers every body)" ocelot POST "$OCELOT_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop ocelot >/dev/null 2>&1 || true

    up_apisix apisix-retry config-default
    bench_shape "s1 apisix (retries=2, buffers POST regardless)" apisix POST "$APISIX_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop apisix >/dev/null 2>&1 || true

    up_envoy envoy-retry
    bench_shape "s1 envoy (retries=2, buffers POST regardless)" envoy POST "$ENVOY_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop envoy >/dev/null 2>&1 || true
}

# ── s2: streaming-only, optimized. 1 MB POST, no retries anywhere, nobody may buffer. ─────────
# NOT out-of-box for APISIX: it needs proxy_request_buffering off to qualify, which is a non-default
# nginx_config snippet (config-stream.yaml) — and nginx documents the cost: a request that is not
# buffered can never be retried, so an APISIX configured like this CANNOT do retry routes at all.
# ConduitSharp and Ocelot run their stock configs; streaming already is their default.
s2_stream_optimized() {
    ensure_payload_1mb
    echo "" | tee -a "$RESULTS"
    echo "== s2: streaming-only (apisix de-tuned: no retry possible) — 1 MB POST, c=$CONNS ==" | tee -a "$RESULTS"

    up scenario-a
    bench_shape "s2 conduitsharp (stream)" gateway POST "$GATEWAY_URL" /payload/1mb.bin 1

    up_competitor ocelot "http://127.0.0.1:8083/bench"
    bench_shape "s2 ocelot (stream)" ocelot POST "$OCELOT_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop ocelot >/dev/null 2>&1 || true

    up_apisix apisix config-stream
    bench_shape "s2 apisix (proxy_request_buffering off — non-default, forfeits retry)" apisix POST "$APISIX_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop apisix >/dev/null 2>&1 || true

    up_envoy envoy-stream
    bench_shape "s2 envoy (stream)" envoy POST "$ENVOY_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop envoy >/dev/null 2>&1 || true
}

# ── s5: the buffered head-to-head again, but with both sides spilling to RAM. ─────────────────
# s4 has both writing to real disk, and APISIX wins there ~2.6x: nginx writes request bodies inline
# in its event loop, while .NET has no true async file I/O on Unix and dispatches every spill write
# to the thread pool. Point both spills at a tmpfs and that I/O disappears for both — which isolates
# how much of s4 was the storage rather than the gateway. (Answer: all of it, and then some.)
#
# Both sides get an identically sized tmpfs: the gateway's SpillDirectory and nginx's
# client_body_temp, mounted by compose. Size matters — Docker's default /dev/shm is 64 MB, and a
# spill-heavy run against that is not fast, it is a flood of 500s that merely looks fast.
#
# tmpfs pages count against the container's mem_limit, so GW_MEM must cover the tmpfs AND the heap;
# 1g with a 512m tmpfs is the shape this was measured at.
s5_tmpfs() {
    ensure_payload_1mb
    echo "" | tee -a "$RESULTS"
    echo "== s5: buffered on tmpfs — 1 MB PUT, c=$CONNS, both spill to RAM, GW_MEM=$GW_MEM ==" | tee -a "$RESULTS"

    # RAM tier left at its default: this measures the spill path, not the tier.
    SPILL_DIR=/spill-tmpfs MAX_MEMORY_BUFFERED="${MAX_MEMORY_BUFFERED:-67108864}" \
        up scenario-flood "${MAX_TOTAL_BUFFERED:-402653184}"
    bench_shape "s5 conduitsharp (spill -> tmpfs)" gateway PUT "$GATEWAY_URL" /payload/1mb.bin 1

    up_apisix apisix-retry config-default   # client_body_temp is tmpfs via compose
    bench_shape "s5 apisix (client_body_temp -> tmpfs)" apisix PUT "$APISIX_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop apisix >/dev/null 2>&1 || true

    up_envoy envoy-retry
    bench_shape "s5 envoy (buffers entirely in RAM)" envoy PUT "$ENVOY_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop envoy >/dev/null 2>&1 || true
}

# ── s3: ConduitSharp alone — does the shed-vs-die config hold inside a GW_MEM pod? ────────────
# Not a comparison: nothing else on this rig has a buffering budget to compare against. The budget
# is deliberately tight so it actually binds — a 503 is the pass, an OOM-kill is the failure.
s3_shed() {
    rig_gate
    ensure_payload_1mb
    echo "" | tee -a "$RESULTS"
    echo "== s3: conduitsharp graceful shed — 1 MB PUT ramp vs a ${GW_MEM} pod ==" | tee -a "$RESULTS"
    MAX_MEMORY_BUFFERED="${MAX_MEMORY_BUFFERED:-16777216}" up scenario-flood "${MAX_TOTAL_BUFFERED:-67108864}"
    PTF_PAYLOAD=/payload/1mb.bin push_to_failure "s3 conduitsharp (shed vs ${GW_MEM} pod)" "$GATEWAY_URL" gateway
}

# ── s4: the buffered head-to-head. PUT (idempotent, so ConduitSharp really buffers). ──────────
# ConduitSharp is configured to match APISIX's defaults rather than to flatter itself: nginx gives
# each body client_body_buffer_size (16k) of RAM, then spills to disk with no global cap and no
# load-shed. So MemoryBufferThresholdBytes=16k and MaxTotalBufferedBodyBytes=0 (unlimited, never
# 503). Both sides then do the same thing — buffer every upload to disk and serve it — and the
# numbers read like-for-like instead of reflecting a policy difference.
s4_buffered() {
    ensure_payload_1mb
    echo "" | tee -a "$RESULTS"
    echo "== s4: buffered head-to-head — 1 MB PUT, c=$CONNS, conduit configured like apisix ==" | tee -a "$RESULTS"

    MEMORY_THRESHOLD=16384 MAX_MEMORY_BUFFERED=134217728 up scenario-flood 0
    bench_shape "s4 conduitsharp (retry, 16k threshold, no budget cap)" gateway PUT "$GATEWAY_URL" /payload/1mb.bin 1

    up_apisix apisix-retry config-default
    bench_shape "s4 apisix (retries=2, stock buffering)" apisix PUT "$APISIX_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop apisix >/dev/null 2>&1 || true

    up_envoy envoy-retry
    bench_shape "s4 envoy (retries=2, stock buffering)" envoy PUT "$ENVOY_URL" /payload/1mb.bin 1
    "${COMPOSE[@]}" stop envoy >/dev/null 2>&1 || true
}

s6_logging() {
    local S6_REQS="${S6_REQS:-5000}"
    local S6_CONNS="${S6_CONNS:-50}"

    header "s6: logging — $S6_REQS fixed requests, 4 KB POST, to Loki"
    echo "== s6: logging — $S6_REQS fixed requests, 4 KB POST, to Loki ==" | tee -a "$RESULTS"

    ensure_payload_4kb
    dotnet publish ../../examples/ConduitSharp.Plugin.BodyCapture/src/ConduitSharp.Plugin.BodyCapture -c Release -o plugins/bench-logging

    # Loki log counter helper — reads loki_distributor_lines_received_total from :3100/metrics
    loki_count() {
        python3 -c "
import urllib.request
try:
    lines = urllib.request.urlopen('http://127.0.0.1:3100/metrics').read().decode().splitlines()
    print(int(sum(float(l.split()[1]) for l in lines if l.startswith('loki_distributor_lines_received_total{'))))
except Exception:
    print(-1)
" 2>/dev/null
    }

    # Drain: wait until Loki's received counter stops growing (collector queue flushed). Cap 120s.
    drain_loki() {
        echo "   draining otel-collector queue to Loki ..."
        local waited=0 prev=-1 stable=0
        while [ "$waited" -lt 120 ]; do
            sleep 5; waited=$((waited + 5))
            local cur; cur=$(loki_count)
            if [ "$prev" != "-1" ] && [ "$cur" != "-1" ]; then
                local delta=$((cur - prev))
                echo "   drain: +${delta} logs in last 5s (total ${cur})"
                if [ "$delta" -lt 100 ]; then
                    stable=$((stable + 1)); [ "$stable" -ge 2 ] && break
                else stable=0; fi
            fi
            prev=$cur
        done
    }

    # One fixed-count arm: start gateway, send N requests, drain, report ingestion %.
    # $1=label $2=service $3=url
    bench_logging_arm() {
        local label="$1" svc="$2" url="$3"
        rig_gate

        # Warmup, then drain so warmup logs don't leak into the measurement
        "${COMPOSE[@]}" run --rm -T --quiet-pull load --print r --format json -l \
            -c "$S6_CONNS" -n 500 -m POST -f /payload/4kb.bin -H "Content-Type: application/octet-stream" "$url" \
            >/dev/null 2>&1 || true
        echo "   draining warmup logs..."
        drain_loki

        local before_loki; before_loki=$(loki_count)

        echo ">> $label [$S6_REQS reqs, c=$S6_CONNS]"
        local json; json=$(mktemp)
        "${COMPOSE[@]}" run --rm -T --quiet-pull load --print r --format json -l \
            -c "$S6_CONNS" -n "$S6_REQS" -m POST -f /payload/4kb.bin -H "Content-Type: application/octet-stream" "$url" \
            > "$json" 2>/dev/null || true

        drain_loki
        local after_loki; after_loki=$(loki_count)

        # Extract results from bombardier JSON
        python3 - "$label" "$json" "$before_loki" "$after_loki" "$S6_REQS" "$RESULTS" <<'PY'
import json, sys

label, jpath, before_s, after_s, total_reqs_s, results_file = sys.argv[1:]
before, after = int(before_s), int(after_s)
total_reqs = int(total_reqs_s)

try:
    data = json.load(open(jpath))["result"]
    qps    = data["rps"]["mean"]
    lat_ms = data["latency"]["mean"] / 1e3   # us -> ms
    p50_ms = data["latency"]["percentiles"]["50"] / 1e3
    p99_ms = data["latency"]["percentiles"]["99"] / 1e3
    ok_2xx = data.get("req2xx", 0)
    elapsed = total_reqs / qps if qps > 0 else 0
except Exception as e:
    print(f"   ERROR parsing {jpath}: {e}", file=sys.stderr)
    qps = lat_ms = p50_ms = p99_ms = ok_2xx = elapsed = 0

ingested = (after - before) if before >= 0 and after >= 0 else -1
generated = total_reqs  # 1 log per request
pct = f"{ingested / generated * 100:.1f}%" if ingested >= 0 and generated > 0 else "n/a"

print(f"    -> {label}: {ok_2xx}/{total_reqs} requests OK in {elapsed:.1f}s ({qps:.0f} QPS)")
print(f"    -> {label}: p50 {p50_ms:.2f} ms, p99 {p99_ms:.2f} ms")
print(f"    -> {label}: logs generated {generated:,} / ingested {ingested:,} = {pct} completion")

with open(results_file, "a") as f:
    f.write(f"    -> {label}: {ok_2xx}/{total_reqs} OK in {elapsed:.1f}s ({qps:.0f} QPS), "
            f"p50 {p50_ms:.2f}ms p99 {p99_ms:.2f}ms, "
            f"logs {ingested:,}/{generated:,} = {pct}\n")
PY
        rm -f "$json"
    }

    # Fresh observability stack per arm — isolates Loki counters
    reset_obs() {
        echo "   resetting observability stack..."
        "${COMPOSE[@]}" -f docker-compose.yml -f docker-compose.loki.yml down -v --remove-orphans >/dev/null 2>&1 || true
        docker volume rm load_envoy-logs >/dev/null 2>&1 || true
        docker system prune -f --volumes >/dev/null 2>&1 || true
        "${COMPOSE[@]}" -f docker-compose.yml -f docker-compose.loki.yml up -d loki otel-collector tempo promtail >/dev/null 2>&1
        sleep 5
    }

    # --- Conduit ---
    reset_obs
    LOG_LEVEL=Warning OTEL_ENDPOINT="http://otel-collector:4317" up scenario-logging
    bench_logging_arm "s6 conduitsharp (capture plugin -> OTLP -> Loki)" gateway "$GATEWAY_URL"
    "${COMPOSE[@]}" stop gateway >/dev/null 2>&1 || true

    # --- Ocelot ---
    reset_obs
    OCELOT_CAPTURE_BODY=1 LOG_LEVEL=Warning OTEL_ENDPOINT="http://otel-collector:4317" up_competitor ocelot "http://127.0.0.1:8083/bench"
    bench_logging_arm "s6 ocelot (custom middleware -> OTLP -> Loki)" ocelot "$OCELOT_URL"
    "${COMPOSE[@]}" stop ocelot >/dev/null 2>&1 || true

    # --- APISIX ---
    reset_obs
    up_apisix apisix-logging config-default
    bench_logging_arm "s6 apisix (loki-logger plugin -> Loki)" apisix "$APISIX_URL"
    "${COMPOSE[@]}" stop apisix >/dev/null 2>&1 || true

    # --- Envoy ---
    reset_obs
    up_envoy envoy-logging
    bench_logging_arm "s6 envoy (tap filter -> Promtail -> Loki)" envoy "$ENVOY_URL"
    "${COMPOSE[@]}" stop envoy >/dev/null 2>&1 || true

    "${COMPOSE[@]}" -f docker-compose.yml -f docker-compose.loki.yml stop loki otel-collector tempo promtail >/dev/null 2>&1 || true
}

# Phase 3: long fixed-rate soak; RSS at start vs end ≈ no leak.
soak() {
    up scenario-a
    local flag mem; flag=$(mktemp); mem=$(mktemp)
    sample_mem gateway "$flag" "$mem" &
    bench "soak $SOAK_DUR @ rate=$SOAK_RATE" -c "$CONNS" -d "$SOAK_DUR" --rate "$SOAK_RATE" "$GATEWAY_URL"
    rm -f "$flag"; wait || true
    echo "soak: max gateway RSS $(cat "$mem") MiB" | tee -a "$RESULTS"
}

# Create the bind-mounted payload dir BEFORE any compose command touches it —
# otherwise the Docker daemon creates it root-owned and flood's dd can't write.
mkdir -p payload


# Pull the load-gen image up front so pull progress never lands in a JSON pipe.
"${COMPOSE[@]}" pull -q load >/dev/null

case "${1:-all}" in
    direct)     header; scenario_direct ;;
    scenario-a) header; scenario_a ;;
    scenario-b) header; scenario_b ;;
    ocelot)     header; scenario_ocelot ;;
    apisix)     header; scenario_apisix ;;
    flood)      header; flood ;;
    soak)       header; soak ;;
    # Ramp 6 MB uploads until each gateway breaks — 503 load-shed vs OOM, same mem_limit for all.
    # Grouped by what each does with the body; see the note above push_to_failure.
    push-to-failure)  header; ptf_conduitsharp; ptf_ocelot; ptf_envoy ;;          # stream
    ptf-buffered)     header; ptf_conduitsharp_buffered; ptf_apisix ;; # both buffer to disk
    # The structured comparison. Each fixes the shape of the work, then compares gateways doing it.
    s1) header; s1_out_of_box ;;
    s2) header; s2_stream_optimized ;;
    s3) header; s3_shed ;;
    s4) header; s4_buffered ;;
    s5) header; s5_tmpfs ;;
    s6) header; s6_logging ;;
    matrix) header; s1_out_of_box; s2_stream_optimized; s3_shed; s4_buffered; s5_tmpfs; s6_logging ;;
    ptf-conduitsharp) header; ptf_conduitsharp ;;
    ptf-ocelot)       header; ptf_ocelot ;;
    ptf-apisix)       header; ptf_apisix ;;
    ptf-envoy)        header; ptf_envoy ;;
    all)        header; scenario_direct; scenario_a; scenario_b; flood ;;
    # The money chart: every gateway benched sequentially on the same rig.
    compare)    header; scenario_direct; scenario_a; scenario_ocelot; scenario_apisix; scenario_envoy ;;
    # High-concurrency pass (tail-latency spread): CONNS=512 APPEND=1 ./run.sh compare-hc
    compare-hc) header; scenario_a; scenario_ocelot; scenario_apisix; scenario_envoy ;;
    *) echo "usage: $0 [all|direct|scenario-a|scenario-b|ocelot|apisix|envoy|flood|soak|compare|compare-hc|push-to-failure|ptf-conduitsharp|ptf-ocelot|ptf-apisix|ptf-envoy|ptf-buffered|s1|s2|s3|s4|s5|s6|matrix]" >&2; exit 1 ;;
esac

"${COMPOSE[@]}" down
echo "results → $RESULTS"
