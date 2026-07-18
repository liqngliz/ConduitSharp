"""Inject the head-to-head microbenchmark tables into README's BENCH-MICRO block.

Three comparisons, all in-proc with a real loopback socket to the same 1 KB upstream:
route-table scaling (the DFA-vs-linear-scan story), policy chain (JWT + rate limit),
and upload bodies (buffered vs streamOnly vs Ocelot's streaming).

Usage: python3 readme-micro.py <bdn-results-dir> <ci-run-url> [README.md]
"""
import os
import re
import sys

START, END = "<!-- BENCH-MICRO:START -->", "<!-- BENCH-MICRO:END -->"

SECTIONS = [
    ("GatewayComparisonBenchmarks",
     "Route-table scaling — request hits the last of N routes",
     "ConduitSharp rides ASP.NET endpoint routing's DFA: flat time and allocations at any\n"
     "route count. Ocelot's route finder scans templates per request — cost grows with N."),
    ("GatewayPolicyComparisonBenchmarks",
     "Policy chain — JWT auth (HS256) + rate limit on both sides",
     ""),
    ("GatewayBodyComparisonBenchmarks",
     "Upload bodies — POST (streamed) and PUT on a retry route (buffered)",
     "Both gateways stream a POST upload — retries never apply to a POST, whose body could not\n"
     "be safely replayed, so neither side allocates a buffer. Identical work: the delta is\n"
     "per-request overhead, and ConduitSharp allocates about half.\n"
     "\n"
     "The `-retry` arms are the buffered path, same-on-same: a PUT each side must be able to\n"
     "replay. Ocelot ships no retry, so it runs the load rig's, built on its official Polly\n"
     "package's `AddPolly` seam\n"
     "([BufferingPollyHandler](benchmarks/load/ocelot/BufferingPollyHandler.cs)) — the whole body\n"
     "held on the heap via `LoadIntoBufferAsync`, per in-flight request, no ceiling. ConduitSharp\n"
     "buffers up to 1 MiB in pooled memory and spills the rest to tmpfs on this rig — bounded\n"
     "RAM either way, and the tiers degrade to disk and then 503 instead of OOM under\n"
     "concurrency (measured in [benchmarks/load](benchmarks/load/README.md))."),
]

# Route-scaling: keep only the endpoints N=1 and N=500 — they carry the flat-vs-linear story;
# the N=100 midpoint is redundant in both the table and the chart built from these rows.
ROW_EXCLUDE: dict[str, re.Pattern[str]] = {
    "GatewayComparisonBenchmarks": re.compile(r"\*\*100\*\*"),
}


def _cells(row):
    return [c.strip().strip("*").strip() for c in row.strip("|").split("|")]


def _alloc_kb(cell):
    """Allocated column ('14.33 KB', '20,567.17 KB') -> float KB. Last column in every BDN table."""
    return float(cell.replace(",", "").removesuffix("KB").strip())


def _bar(title, y_title, labels, values):
    """Single-series mermaid bar — the deterministic Allocated column, charted; the table
    right below keeps the exact figures. Bars only, so identity is the x-axis label, not color."""
    return [
        "```mermaid",
        "xychart-beta",
        f'    title "{title}"',
        "    x-axis [{}]".format(", ".join(f'"{l}"' for l in labels)),
        f'    y-axis "{y_title}" 0 --> {max(values) * 1.15:.2f}',
        "    bar [{}]".format(", ".join(f"{v:.2f}" for v in values)),
        "```",
        "",
    ]


def chart_for(basename, rows):
    """Allocation bar tuned to each comparison's table shape — None when a section has no chart."""
    body = rows[2:]  # skip header + separator
    if basename == "GatewayComparisonBenchmarks":  # cols: Method Gateway RouteCount ... Allocated
        return _bar("Allocated per request, N routes configured — lower is better", "KB / request",
                    [f"{_cells(r)[1]} N={_cells(r)[2]}" for r in body],
                    [_alloc_kb(_cells(r)[-1]) for r in body])
    if basename == "GatewayPolicyComparisonBenchmarks":  # cols: Method Gateway ... Allocated
        return _bar("Allocated per request — JWT + rate limit, lower is better", "KB / request",
                    [_cells(r)[1] for r in body],
                    [_alloc_kb(_cells(r)[-1]) for r in body])
    if basename == "GatewayBodyComparisonBenchmarks":  # cols: Method Gateway BodyKB ... Allocated
        # 10 MB arms only: the buffered divergence. At a big body the streamed arms track the body
        # (~10 MB) while Ocelot's retry holds a SECOND heap copy (~20 MB).
        big = [r for r in body if _cells(r)[2] == "10240"]
        return _bar("Allocated per request, 10 MB body — lower is better", "KB / request",
                    [_cells(r)[1] for r in big],
                    [_alloc_kb(_cells(r)[-1]) for r in big])
    return None

results_dir, run_url = sys.argv[1], sys.argv[2]
readme_path = sys.argv[3] if len(sys.argv) > 3 else "README.md"

parts = [
    START,
    "### Head-to-head microbenchmarks — ConduitSharp vs Ocelot (.NET gateways only)",
    "",
]
for basename, title, note in SECTIONS:
    path = os.path.join(results_dir, f"ConduitSharp.Benchmarks.{basename}-report-github.md")
    if not os.path.exists(path):
        continue
    drop = ROW_EXCLUDE.get(basename)
    rows = [l for l in open(path).read().splitlines() if l.startswith("|")]
    if drop:
        rows = rows[:2] + [r for r in rows[2:] if not drop.search(r)]  # keep header + separator
    table = "\n".join(rows)
    if not table:
        continue
    parts += [f"#### {title}", ""]
    chart = chart_for(basename, rows)
    if chart:
        parts += chart
    parts += [table, ""]
    if note:
        parts += [note, ""]

if len(parts) <= 3:
    sys.exit(f"no comparison tables found in {results_dir} — refusing to publish")

parts += [
    "Both gateways in-proc (TestServer), forwarding over a real loopback socket to the same",
    "1 KB upstream — identical downstream cost, the delta is gateway overhead.",
    "**Allocated per request is deterministic — compare that column;** time columns are",
    "trend-only on shared CI runners. APISIX is nginx/Lua and cannot be micro-benched",
    "in-process; its comparison is the throughput ratio table above. Full tables:",
    f"[docs/benchmarks/micro.md](docs/benchmarks/micro.md) · [source run]({run_url})",
    END,
]
block = "\n".join(parts)

readme = open(readme_path).read()
head, _, rest = readme.partition(START)
_, _, tail = rest.partition(END)
if not rest:
    sys.exit(f"README has no {START} marker")
open(readme_path, "w").write(head + block + tail)
print(block)
