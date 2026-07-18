"""Format one bombardier JSON result (stdin) as a results.md row + a runs.jsonl record.

Usage: bombardier --format json ... | python3 row.py <label> <results.md> <runs.jsonl>
                                        [--ratio R] [--spread S] [--reps N]

Statuses are reported separately on purpose. A 503 is the gateway deliberately shedding load —
the pass condition for the flood/shed scenarios — while `conn` is bombardier failing to complete
a request at all (timeout, reset, refused), which is usually the rig rather than the gateway.
Collapsing those into one "errors" number makes a healthy load-shed and a falling-over gateway
look identical, so they get their own columns.

The optional flags carry what a single bombardier result cannot know, and what a table rendered
from this file cannot be honest without:

  --ratio   bytes the gateway wrote to STORAGE / bytes uploaded through it. Evidence of a
            disk-backed buffer: a streaming proxy writes ~0, a spilling one ~1 copy per body.
            Blind to an in-RAM buffer — Ocelot buffers whole bodies in heap and still reads 0.00x.
  --spread  +/-% across the measured reps. Whether the number is quotable at all.
  --reps    how many runs the median came from.

Without them these live only in results.md prose, so any generator reading runs.jsonl would have
to re-derive them by parsing markdown, or quietly drop the columns that carry the finding.
"""
import json
import sys

argv = sys.argv[1:]
opts = {}
for flag in ("--ratio", "--spread", "--reps"):
    if flag in argv:
        i = argv.index(flag)
        opts[flag.lstrip("-")] = argv[i + 1]
        del argv[i:i + 2]

label, results_path, jsonl_path = argv[0], argv[1], argv[2]

r = json.load(sys.stdin)["result"]
lat = r["latency"]  # bombardier reports microseconds
rec = {
    "label":   label,
    "qps":     r["rps"]["mean"],
    "mean_ms": lat["mean"] / 1000,
    "p50_ms":  lat["percentiles"]["50"] / 1000,
    "p99_ms":  lat["percentiles"]["99"] / 1000,
    "ok":      r["req2xx"],
    "c4xx":    r["req4xx"],
    "c5xx":    r["req5xx"],
    "conn":    r["others"],
}
if "ratio" in opts:
    rec["write_ratio"] = float(opts["ratio"])
    rec["spilled_to_disk"] = float(opts["ratio"]) > 0.5
if "spread" in opts:
    rec["spread_pct"] = float(opts["spread"])
if "reps" in opts:
    rec["reps"] = int(opts["reps"])

row = ("| {label} | {qps:.0f} | {mean_ms:.2f} | {p50_ms:.2f} | {p99_ms:.2f} | {ok} | {c4xx} | {c5xx} | {conn} |"
       .format(**rec))
with open(results_path, "a") as f:
    f.write(row + "\n")
with open(jsonl_path, "a") as f:
    f.write(json.dumps(rec) + "\n")
print(row)
