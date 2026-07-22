# Latest CI benchmark run (raw figures)

> Shared GitHub Actions runner (4 vCPU): absolute numbers are run-to-run noise;
> the published claim is the README's same-rig ratio table.
> Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29898589358


## 2026-07-22T06:59:54Z — DUR=60s CONNS=125 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| direct-to-upstream (no gateway) [c=125] | 148625 | 0.84 | 0.72 | 2.98 | 8917551 | 0 | 0 | 0 |
| scenario-a pure proxy (max QPS) [c=125] | 32869 | 3.80 | 3.31 | 12.82 | 1971080 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=125] | 21284 | 5.87 | 5.25 | 18.04 | 1276682 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=125] | 35411 | 3.53 | 3.26 | 8.95 | 2124654 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=125] | 26706 | 4.68 | 4.45 | 9.08 | 1602314 | 0 | 0 | 0 |

## 2026-07-22T07:10:17Z — DUR=60s CONNS=512 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| scenario-a pure proxy (max QPS) [c=512] | 33584 | 15.35 | 13.10 | 52.55 | 2001030 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=512] | 22404 | 23.00 | 21.14 | 67.53 | 1335617 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=512] | 34535 | 14.83 | 13.10 | 44.72 | 2071896 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=512] | 26112 | 19.61 | 19.19 | 33.28 | 1566812 | 0 | 0 | 0 |
