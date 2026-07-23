# Latest CI benchmark run (raw figures)

> Shared GitHub Actions runner (4 vCPU): absolute numbers are run-to-run noise;
> the published claim is the README's same-rig ratio table.
> Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29957816337


## 2026-07-22T21:05:25Z — DUR=60s CONNS=125 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| direct-to-upstream (no gateway) [c=125] | 92133 | 1.35 | 1.20 | 4.54 | 5527627 | 0 | 0 | 0 |
| scenario-a pure proxy (max QPS) [c=125] | 21211 | 5.89 | 5.07 | 20.61 | 1272287 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=125] | 14658 | 8.53 | 7.75 | 23.96 | 879076 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=125] | 22129 | 5.65 | 5.25 | 14.36 | 1327798 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=125] | 17004 | 7.35 | 7.08 | 14.23 | 1020222 | 0 | 0 | 0 |

## 2026-07-22T21:16:21Z — DUR=60s CONNS=512 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| scenario-a pure proxy (max QPS) [c=512] | 22021 | 23.45 | 20.58 | 71.77 | 1309904 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=512] | 15239 | 34.22 | 31.21 | 90.25 | 897761 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=512] | 21635 | 23.67 | 22.41 | 61.29 | 1297605 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=512] | 16579 | 30.89 | 29.60 | 55.82 | 994628 | 0 | 0 | 0 |
