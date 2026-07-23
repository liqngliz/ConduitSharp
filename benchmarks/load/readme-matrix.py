"""Render the s1..s5 structured-comparison tables from the matrix job's runs.jsonl.

Usage: python3 readme-matrix.py <runs.jsonl> <target.md> <ci-run-url> [summary.md]

With the optional fourth argument, also renders a compact relative-QPS summary of the same
records into that file's BENCH-MATRIX-SUMMARY block — the front-README version: ratios only
(ConduitSharp = 1.00x), because on shared CI the ratios are the result and the absolutes are
noise. Same records, same run, so the two blocks cannot disagree.

These tables were hand-typed from a laptop for months, which is exactly how a benchmark table
drifts from the benchmark. The matrix job measures them under the validated protocol (rig gate,
discarded warmup, median of REPS with the spread recorded); this turns that output into the
published tables so the two cannot disagree.

Every column comes from the record, including the two that carry the finding:

  write_ratio  bytes written to STORAGE / bytes uploaded. Evidence of a disk-backed buffer: a
               gateway that streams writes ~0, one that spills writes ~one copy per body.
               It cannot see an in-RAM buffer — Ocelot's LoadIntoBufferAsync holds the whole body
               in heap and still reads 0.00x here — so the column is titled "spilled to disk?",
               not "buffered?". A 0.00x row means "did not touch storage", never "did not buffer".
  spread_pct   +/-% across reps. Over ±15% the row is marked unquotable rather than dropped —
               silence about a noisy measurement is worse than the noisy measurement.

Rows whose scenario has no table here (s3's shed ramp — categorical, not throughput) are ignored.
"""
import json
import re
import sys

# scenario -> (table heading, group). Group splits the streaming scenarios (nobody buffers)
# from the buffered-to-storage ones (s4/s5), which get a throughput bar chart each.
SECTIONS = [
    ("s1", "s1 — out of the box: retries set, 1 MB POST", "stream"),
    ("s2", "s2 — streaming-only, optimized, 1 MB POST", "stream"),
    ("s4", "s4 — buffered on disk, 1 MB PUT", "buffer"),
    ("s5", "s5 — spill target is tmpfs, 1 MB PUT", "buffer"),
    ("s6", "s6 — logging + body capture, 24 KB POST", "logging"),
]

GROUP_HEADINGS = {
    "stream": "#### Streaming path — a 1 MB body nobody needs to replay",
    "buffer": "#### Buffered path — a 1 MB PUT each side must replay (charted: the disk story)",
    "logging": "#### Body Capture Logging — a 24 KB POST logged to Loki",
}


def bar(title, y_title, labels, values):
    """Single-series mermaid throughput bar — the table below keeps the exact figures."""
    return [
        "```mermaid",
        "xychart-beta",
        f'    title "{title}"',
        "    x-axis [{}]".format(", ".join(f'"{l}"' for l in labels)),
        f'    y-axis "{y_title}" 0 --> {max(values) * 1.15:.0f}',
        "    bar [{}]".format(", ".join(f"{v:.0f}" for v in values)),
        "```",
        "",
    ]

START, END = "<!-- BENCH-MATRIX:START -->", "<!-- BENCH-MATRIX:END -->"

jsonl_path, target_path, run_url = sys.argv[1], sys.argv[2], sys.argv[3]

records = []
for line in open(jsonl_path):
    line = line.strip()
    if line:
        try:
            records.append(json.loads(line))
        except json.JSONDecodeError:
            pass

# Labels are presentation text that happens to be stored inside measurement records. When a
# label is reworded in run.sh, artifacts measured before the rename still carry the old text —
# and report-regenerate exists precisely to re-render old artifacts, so apply renames here or
# every regeneration silently reverts the wording. Measurements are never touched.
RELABEL = {
    "hand-added retry": "retry via official Polly seam",
}
for r in records:
    for old, new in RELABEL.items():
        r["label"] = r.get("label", "").replace(old, new)


def gateway_of(label):
    """'s5 conduitsharp (spill -> tmpfs) [c=96 med/3]' -> 'conduitsharp (spill -> tmpfs)'."""
    body = re.sub(r"^s\d+\s+", "", label)
    return re.sub(r"\s*\[c=.*$", "", body).strip()


parts = [START, "### Structured comparison — measured, not hand-typed", ""]
rendered = 0
conns = next((m.group(1) for r in records
              for m in [re.search(r"\[c=(\d+)", r.get("label", ""))] if m), "?")

current_group = None
for scenario, heading, group in SECTIONS:
    rows = [r for r in records if r.get("label", "").startswith(scenario + " ")]
    if not rows:
        continue
    if group != current_group:
        parts += [GROUP_HEADINGS[group], ""]
        current_group = group
    # Buffered scenarios: chart throughput before the table so the disk cost reads at a glance.
    if group == "buffer":
        parts += bar(f"{scenario} — throughput, 1 MB PUT (higher is faster)", "QPS (med/{})".format(rows[0].get("reps", "?")),
                     [gateway_of(r["label"]) for r in rows], [r["qps"] for r in rows])
    if group == "logging":
        parts += [f"| {heading} | QPS (med/{rows[0].get('reps', '?')}) | p99 ms | peak mem | % ingested |",
                  "|---|---:|---:|---:|---:|"]
        for r in rows:
            peak = r.get("peak_mem_kb", "—")
            peak_cell = f"{peak} KB" if peak != "—" else peak
            pct = "—"
            if "ingested" in r and "generated" in r and r["generated"] > 0 and r["ingested"] >= 0:
                pct = f"{r['ingested'] / r['generated'] * 100:.1f}%"
            spread = r.get("spread_pct")
            noisy = " ⚠️" if spread is not None and spread > 15 else ""
            parts.append(f"| {gateway_of(r['label'])} | {r['qps']:.0f}{noisy} | {r['p99_ms']:.0f} "
                         f"| {peak_cell} | {pct} |")
    else:
        parts += [f"| {heading} | QPS (med/{rows[0].get('reps', '?')}) | p99 ms | written÷uploaded | spilled to disk? |",
                  "|---|---:|---:|---:|---|"]
        for r in rows:
            ratio = r.get("write_ratio")
            # A record without the ratio predates it being recorded; say so rather than print a 0.00x
            # that would read as proof of streaming.
            ratio_cell = f"{ratio:.2f}x" if ratio is not None else "—"
            # Derive from the ratio, never from a stored bool: the ratio is the measurement, the bool
            # is a convenience that a record written before the field was named this way will not have
            # — and a missing bool would silently render "no" over a 1.96x row.
            spilled = "—" if ratio is None else ("**yes**" if ratio > 0.5 else "no")
            spread = r.get("spread_pct")
            noisy = " ⚠️" if spread is not None and spread > 15 else ""
            parts.append(f"| {gateway_of(r['label'])} | {r['qps']:.0f}{noisy} | {r['p99_ms']:.0f} "
                         f"| {ratio_cell} | {spilled} |")
    parts.append("")
    rendered += 1

if not rendered:
    sys.exit(f"no s1..s5 records in {jsonl_path} — refusing to publish")

if any(r.get("spread_pct", 0) > 15 for r in records):
    parts += ["⚠️ marks a row whose reps spread more than ±15% — rig noise, not a result. "
              "Re-run rather than quote it.", ""]

parts += [
    f"Median of {records[0].get('reps', '?')} runs at c={conns} on a shared GitHub Actions runner "
    "(4 vCPU), each behind the rig gate and a discarded warmup — see "
    "[What makes a run of this matrix valid](#what-makes-a-run-of-this-matrix-valid). "
    "**Ratios travel; absolute QPS on shared CI does not.** written÷uploaded measures writes to "
    "*storage*: a 0.00x row did not touch disk, which is not the same as not buffering — an "
    "in-RAM buffer (Ocelot's LoadIntoBufferAsync) is invisible to it and shows as its throughput "
    "cost instead. "
    f"Raw figures: [CI run]({run_url}).",
    END,
]
block = "\n".join(parts)

target = open(target_path).read()
head, _, rest = target.partition(START)
if not rest:
    sys.exit(f"{target_path} has no {START} marker")
_, _, tail = rest.partition(END)
open(target_path, "w").write(head + block + tail)
print(block)

# ---- optional compact summary for the front README ----
if len(sys.argv) > 4:
    summary_path = sys.argv[4]
    S_START, S_END = "<!-- BENCH-MATRIX-SUMMARY:START -->", "<!-- BENCH-MATRIX-SUMMARY:END -->"
    # Two groups, mirroring the full table: streaming scenarios (nobody buffers) and the
    # buffered-to-storage ones (s4/s5), where the tmpfs bar carries the finding.
    S_GROUPS = [
        ("#### Streaming path — a 1 MB body nobody needs to replay", [
            ("s1", "s1 — retries configured, 1 MB POST (ConduitSharp streams it: method-aware)"),
            ("s2", "s2 — pure streaming, 1 MB POST (APISIX de-tuned to qualify, forfeiting retry)"),
        ]),
        ("#### Buffered path — forced to disk (s4), and tmpfs as the answer (s5)", [
            ("s4", "s4 — buffering forced onto disk, 1 MB PUT"),
            ("s5", "s5 — buffered, spill target is tmpfs, 1 MB PUT"),
        ]),
        ("#### Body Capture Logging — a 24 KB POST logged to Loki", [
            ("s6", "s6 — logging + body capture, 24 KB POST"),
        ]),
    ]

    def find(rows, name):
        return next((r for r in rows if gateway_of(r["label"]).startswith(name)), None)

    s_parts = [S_START,
               "### Body handling under load — the s1..s5 matrix (relative QPS, same rig)",
               ""]

    def cell(r, base):
        if r is None:
            return "—"
        rel = f"{r['qps'] / base:.2f}×"
        ratio = r.get("write_ratio")
        if ratio is not None and ratio > 0.5:
            rel += f" ({ratio:.1f}x to disk)"
        return rel

    s_rendered = 0
    for group_heading, group_rows in S_GROUPS:
        s_parts += [group_heading, ""]

        # Streaming bar: the out-of-the-box scenario (s1) — how each gateway ships. Relative QPS,
        # ConduitSharp = 1.00. All three gateways, since Ocelot has an s1 arm.
        if group_heading.startswith("#### Streaming"):
            s1 = [r for r in records if r.get("label", "").startswith("s1 ")]
            s1_cs = find(s1, "conduitsharp")
            if s1_cs is not None:
                labels, values = [], []
                for name, display in (("conduitsharp", "ConduitSharp"), ("ocelot", "Ocelot"), ("apisix", "APISIX"), ("envoy", "Envoy")):
                    r = find(s1, name)
                    if r is not None:
                        labels.append(display)
                        values.append(r["qps"] / s1_cs["qps"])
                s_parts += [
                    "```mermaid",
                    "xychart-beta",
                    '    title "s1 — out of the box, retries set, 1 MB POST: relative QPS (higher is faster)"',
                    "    x-axis [{}]".format(", ".join(f'"{l}"' for l in labels)),
                    f'    y-axis "QPS vs ConduitSharp = 1.00" 0 --> {max(values) * 1.15:.2f}',
                    "    bar [{}]".format(", ".join(f"{v:.2f}" for v in values)),
                    "```",
                    "",
                ]

        # Buffered bar: ONE baseline — ConduitSharp forced to real disk (s4) = 1.00 — so all four
        # bars share a y-axis and read as one story. Both gateways appear on disk (s4) and tmpfs (s5).
        # APISIX barely moves disk→tmpfs (2.55→2.46: nginx writes client_body_temp inline either way),
        # while ConduitSharp jumps (1.00→3.26: the RAM tier means the 1 MB body rarely reaches the
        # spill file at all). Per-scenario baselines would have hidden this by giving each APISIX bar
        # a different denominator — making nginx look like it slowed down when only our number moved.
        if group_heading.startswith("#### Buffered"):
            s4 = [r for r in records if r.get("label", "").startswith("s4 ")]
            s5 = [r for r in records if r.get("label", "").startswith("s5 ")]
            cs_disk, ap_disk, en_disk = find(s4, "conduitsharp"), find(s4, "apisix"), find(s4, "envoy")
            cs_tmpfs, ap_tmpfs, en_tmpfs = find(s5, "conduitsharp"), find(s5, "apisix"), find(s5, "envoy")
            if cs_disk and ap_disk and cs_tmpfs and ap_tmpfs and en_disk and en_tmpfs:
                base = cs_disk["qps"]
                values = [1.0, ap_disk["qps"] / base, en_disk["qps"] / base, en_tmpfs["qps"] / base, ap_tmpfs["qps"] / base, cs_tmpfs["qps"] / base]
                s_parts += [
                    "```mermaid",
                    "xychart-beta",
                    '    title "1 MB PUT, disk vs tmpfs spill: relative QPS (higher is faster)"',
                    '    x-axis ["ConduitSharp — disk", "APISIX — disk", "Envoy — disk", "Envoy — tmpfs", "APISIX — tmpfs", "ConduitSharp — tmpfs"]',
                    f'    y-axis "QPS vs ConduitSharp disk = 1.00" 0 --> {max(values) * 1.15:.2f}',
                    "    bar [{}]".format(", ".join(f"{v:.2f}" for v in values)),
                    "```",
                    "",
                ]

        s_parts += [f"| scenario (c={conns}) | ConduitSharp | Ocelot | APISIX | Envoy |",
                    "|---|---:|---:|---:|---:|"]
        for scenario, heading in group_rows:
            rows = [r for r in records if r.get("label", "").startswith(scenario + " ")]
            conduit = find(rows, "conduitsharp")
            if conduit is None:
                continue
            base = conduit["qps"]
            s_parts.append(f"| {heading} | {cell(conduit, base)} "
                           f"| {cell(find(rows, 'ocelot'), base)} | {cell(find(rows, 'apisix'), base)} | {cell(find(rows, 'envoy'), base)} |")
            s_rendered += 1
        s_parts.append("")

    s_parts += [
        "Structured comparison: each scenario fixes the shape of the work, then compares gateways "
        "doing that shape, with bytes-written-to-storage measured rather than assumed. s4 is the "
        "honest row: forced entirely onto disk, nginx wins — the design's answer is s5 and the RAM "
        "tier that makes disk rare. Full tables, method, and the parts that hurt: "
        f"[benchmarks/load](benchmarks/load/README.md#structured-comparison--measured-not-hand-typed) · [CI run]({run_url}).",
        S_END,
    ]
    if s_rendered:
        s_block = "\n".join(s_parts)
        summary = open(summary_path).read()
        head, _, rest = summary.partition(S_START)
        if not rest:
            sys.exit(f"{summary_path} has no {S_START} marker")
        _, _, tail = rest.partition(S_END)
        open(summary_path, "w").write(head + s_block + tail)
        print(s_block)
