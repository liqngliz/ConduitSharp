"""Reduce a bench_shape arm's reps to the two numbers that decide whether it means anything.

Usage: python3 metrics.py <label> <bytes-before> <bytes-after> <results.md> <payload-MiB> <run.json>...

Prints shell assignments for run.sh to eval:

  RATIO   bytes the gateway wrote to storage / bytes uploaded through it. The evidence of *who
          buffered*: a streaming proxy writes ~0, a buffering one writes ~one copy per body. A
          ratio, never an absolute — a busy run moves tens of GiB, so any fixed byte threshold
          calls everything a buffer.
  SPREAD  +/-% across the reps. Whether the median is quotable at all; this rig has produced 223
          and 345 QPS from identical config back to back.

Also appends the human-readable verdict to results.md.

This lives in a file rather than a heredoc inside run.sh because the f-string format specs
(`{wrote:,.0f}`) are brace-and-comma syntax, which the shell mangles into `wrote: .0f` when the
heredoc is nested inside a command substitution inside an eval. A script file is immune, and
testable on its own.
"""
import json
import shlex
import sys

label = sys.argv[1]
before, after = int(sys.argv[2]), int(sys.argv[3])
results_path, payload_mib = sys.argv[4], float(sys.argv[5])
run_files = sys.argv[6:]

qps, reqs = [], 0
for path in run_files:
    try:
        r = json.load(open(path))["result"]
        qps.append(r["rps"]["mean"])
        reqs += r["req2xx"] + r["req4xx"] + r["req5xx"] + r["others"]
    except Exception:
        pass

lines, spread = [], 0.0
if len(qps) > 1:
    mid = sorted(qps)[len(qps) // 2]
    spread = (100 * (max(qps) - min(qps)) / mid if mid else 0) / 2  # reported as +/-
    flag = "  ** UNSTABLE — rig noise, do not quote **" if spread * 2 > 30 else ""
    reps = "/".join(f"{q:.0f}" for q in qps)
    lines.append(f"    -> {label}: reps {reps} QPS, spread ±{spread:.0f}%{flag}")

wrote = (after - before) / 1048576
uploaded = reqs * payload_mib
ratio = wrote / uploaded if uploaded else 0
verdict = "BUFFERED the body" if ratio > 0.5 else "no body buffer"
lines.append(f"    -> {label}: wrote {wrote:,.0f} MiB / uploaded {uploaded:,.0f} MiB "
             f"= {ratio:.2f}x -> {verdict}")

with open(results_path, "a") as f:
    f.write("\n".join(lines) + "\n")

print(f"RATIO={ratio:.4f}")
print(f"SPREAD={spread:.1f}")
print("VERDICT_LINES=" + shlex.quote("\n".join(lines)))
