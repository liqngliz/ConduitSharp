# Latest CI benchmark run (raw figures)

> Shared GitHub Actions runner (4 vCPU): absolute numbers are run-to-run noise;
> the published claim is the README's same-rig ratio table.
> Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/30165314699


## 2026-07-25T16:17:13Z — DUR=60s CONNS=125 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| direct-to-upstream (no gateway) [c=125] | 92366 | 1.35 | 1.17 | 4.98 | 5541974 | 0 | 0 | 0 |
| scenario-a pure proxy (max QPS) [c=125] | 20581 | 6.07 | 5.29 | 20.28 | 1234271 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=125] | 14505 | 8.62 | 7.82 | 24.27 | 869816 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=125] | 21329 | 5.86 | 5.47 | 14.93 | 1279758 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=125] | 16971 | 7.36 | 6.94 | 15.09 | 1018300 | 0 | 0 | 0 |

## 2026-07-25T16:28:00Z — DUR=60s CONNS=512 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| scenario-a pure proxy (max QPS) [c=512] | 21389 | 24.34 | 21.35 | 74.43 | 1261800 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=512] | 14716 | 35.77 | 33.04 | 91.43 | 858912 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=512] | 21317 | 24.02 | 22.75 | 58.66 | 1278937 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=512] | 16550 | 30.94 | 30.12 | 53.76 | 993030 | 0 | 0 | 0 |
