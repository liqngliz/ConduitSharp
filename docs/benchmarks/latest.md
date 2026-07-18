# Latest CI benchmark run (raw figures)

> Shared GitHub Actions runner (4 vCPU): absolute numbers are run-to-run noise;
> the published claim is the README's same-rig ratio table.
> Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29659383588


## 2026-07-18T20:16:31Z — DUR=120s CONNS=125 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| direct-to-upstream (no gateway) [c=125] | 70761 | 1.76 | 1.23 | 6.76 | 8492521 | 0 | 0 | 0 |
| scenario-a pure proxy (max QPS) [c=125] | 16707 | 7.49 | 6.86 | 24.88 | 2001390 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=125] | 11181 | 11.19 | 10.32 | 34.30 | 1340303 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=125] | 17285 | 7.23 | 6.72 | 21.29 | 2074046 | 0 | 0 | 0 |

## 2026-07-18T20:28:44Z — DUR=120s CONNS=512 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| scenario-a pure proxy (max QPS) [c=512] | 17165 | 30.67 | 27.32 | 113.60 | 2003222 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=512] | 11775 | 45.09 | 42.29 | 133.00 | 1362481 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=512] | 16148 | 31.71 | 29.11 | 71.33 | 1937628 | 0 | 0 | 0 |
