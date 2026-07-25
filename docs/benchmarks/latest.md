# Latest CI benchmark run (raw figures)

> Shared GitHub Actions runner (4 vCPU): absolute numbers are run-to-run noise;
> the published claim is the README's same-rig ratio table.
> Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/30158839718


## 2026-07-25T12:57:38Z — DUR=60s CONNS=125 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| direct-to-upstream (no gateway) [c=125] | 104618 | 1.19 | 1.03 | 4.48 | 6276357 | 0 | 0 | 0 |
| scenario-a pure proxy (max QPS) [c=125] | 25432 | 4.91 | 4.19 | 18.66 | 1525831 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=125] | 16829 | 7.43 | 6.65 | 21.57 | 1009249 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=125] | 26521 | 4.71 | 4.25 | 12.73 | 1591297 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=125] | 20239 | 6.17 | 5.95 | 11.57 | 1214389 | 0 | 0 | 0 |

## 2026-07-25T13:08:34Z — DUR=60s CONNS=512 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| scenario-a pure proxy (max QPS) [c=512] | 25431 | 20.35 | 17.53 | 67.21 | 1509574 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=512] | 17152 | 30.33 | 27.43 | 82.75 | 1012737 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=512] | 24066 | 21.29 | 17.32 | 74.45 | 1443099 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=512] | 20055 | 25.53 | 24.86 | 44.84 | 1203198 | 0 | 0 | 0 |
