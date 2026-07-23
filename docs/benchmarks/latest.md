# Latest CI benchmark run (raw figures)

> Shared GitHub Actions runner (4 vCPU): absolute numbers are run-to-run noise;
> the published claim is the README's same-rig ratio table.
> Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/30025170098


## 2026-07-23T16:27:37Z — DUR=60s CONNS=125 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| direct-to-upstream (no gateway) [c=125] | 100728 | 1.24 | 1.00 | 5.35 | 6043003 | 0 | 0 | 0 |
| scenario-a pure proxy (max QPS) [c=125] | 24744 | 5.05 | 4.31 | 18.59 | 1484091 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=125] | 16881 | 7.40 | 6.62 | 21.80 | 1012556 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=125] | 26723 | 4.68 | 4.19 | 13.38 | 1603323 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=125] | 21034 | 5.94 | 5.68 | 12.14 | 1262124 | 0 | 0 | 0 |

## 2026-07-23T16:38:18Z — DUR=60s CONNS=512 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| scenario-a pure proxy (max QPS) [c=512] | 25943 | 19.96 | 17.30 | 64.73 | 1538587 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=512] | 17755 | 29.19 | 26.68 | 81.53 | 1052552 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=512] | 26029 | 19.69 | 17.49 | 55.38 | 1560493 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=512] | 20633 | 24.81 | 24.23 | 44.36 | 1237994 | 0 | 0 | 0 |
