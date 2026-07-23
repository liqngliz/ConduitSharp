"""Rewrite the README benchmark block from a compare run's runs.jsonl.

Publishes RATIOS, not absolute QPS: runs happen on shared GitHub Actions
runners where absolute numbers are meaningless run-to-run, but same-rig
sequential ratios are stable. One table per concurrency level found in the
records ([c=N] label suffix). Links the CI run so raw figures stay auditable.

Usage: python3 readme-block.py <runs.jsonl> <README.md> <ci-run-url>
"""
import json
import re
import sys

START, END = "<!-- BENCH:START -->", "<!-- BENCH:END -->"

# label substring → display name, in table order (ConduitSharp is the 1.00× baseline)
GATEWAYS = [
    ("scenario-a pure proxy", "ConduitSharp"),
    ("apisix pure proxy",     "APISIX"),
    ("ocelot pure proxy",     "Ocelot"),
    ("envoy pure proxy",      "Envoy"),
    ("direct-to-upstream",    "*(no gateway — direct to nginx)*"),
]

jsonl_path, readme_path, run_url = sys.argv[1], sys.argv[2], sys.argv[3]


def xychart(title, y_title, labels, values):
    """One single-series mermaid bar chart — GitHub renders it, tables keep the exact figures."""
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

records = [json.loads(line) for line in open(jsonl_path) if line.strip()]

def conns_of(rec):
    m = re.search(r"\[c=(\d+)\]", rec["label"])
    return int(m.group(1)) if m else 0

def is_max_qps(rec, substr):
    return substr in rec["label"] and ("(max QPS)" in rec["label"] or substr == "direct-to-upstream")

def table_for(conns):
    rows, baseline = [], None
    for substr, name in GATEWAYS:
        rec = next((r for r in records if is_max_qps(r, substr) and conns_of(r) == conns), None)
        if rec is None:
            continue
        if name == "ConduitSharp":
            baseline = rec["qps"]
        rows.append((name, rec))
    if baseline is None:
        return []
    chart_names = {"*(no gateway — direct to nginx)*": "no gateway (direct)"}
    lines = [
        f"#### {conns} connections",
        "",
        *xychart(f"Relative QPS at {conns} connections — higher is faster",
                 "QPS vs ConduitSharp = 1.00",
                 [chart_names.get(name, name) for name, _ in rows],
                 [rec["qps"] / baseline for _, rec in rows]),
        "| gateway | QPS (relative) | p50 | p99 |",
        "|---|---:|---:|---:|",
    ]
    for name, rec in rows:
        lines.append("| {} | {:.2f}× | {:.2f} ms | {:.2f} ms |".format(
            name, rec["qps"] / baseline, rec["p50_ms"], rec["p99_ms"]))
    lines.append("")
    return lines

conn_levels = sorted({conns_of(r) for r in records if any(is_max_qps(r, s) for s, _ in GATEWAYS)})
tables = [line for conns in conn_levels for line in table_for(conns)]
if not tables:
    sys.exit("no ConduitSharp (scenario-a) records in runs.jsonl — refusing to publish")

lines = [
    START,
    "### Throughput — relative, same rig, sequential runs",
    "",
    *tables,
    "Pure proxy, 1 KB upstream response, bombardier, gateways benched sequentially on the",
    "identical rig. **Measured on shared GitHub Actions runners (4 vCPU) — only ratios are",
    "meaningful there; absolute QPS on shared CI is noise.** Raw figures for this exact run:",
    f"[CI run]({run_url}). Method & how to reproduce on pinned hardware:",
    "[benchmarks/load](benchmarks/load/README.md).",
    END,
]
block = "\n".join(lines)

readme = open(readme_path).read()
head, _, rest = readme.partition(START)
_, _, tail = rest.partition(END)
if not rest:
    sys.exit(f"README has no {START} marker")
open(readme_path, "w").write(head + block + tail)
print(block)
