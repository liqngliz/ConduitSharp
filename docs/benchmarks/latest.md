# Latest CI benchmark run (raw figures)

> Shared GitHub Actions runner (4 vCPU): absolute numbers are run-to-run noise;
> the published claim is the README's same-rig ratio table.
> Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29581520422


## 2026-07-17T12:47:33Z — DUR=90s CONNS=125 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| direct-to-upstream (no gateway) [c=125] | 41753 | 2.99 | 2.40 | 10.60 | 3756538 | 0 | 0 | 0 |
| scenario-a pure proxy (max QPS) [c=125] | 8885 | 14.13 | 12.98 | 43.50 | 796148 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=125] | 6760 | 18.60 | 17.15 | 51.26 | 604873 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=125] | 10195 | 12.26 | 11.43 | 30.50 | 917312 | 0 | 0 | 0 |

## 2026-07-17T12:58:01Z — DUR=90s CONNS=512 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| scenario-a pure proxy (max QPS) [c=512] | 9176 | 57.82 | 50.95 | 197.31 | 796926 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=512] | 6866 | 76.79 | 70.96 | 200.65 | 600136 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=512] | 9104 | 56.26 | 53.68 | 95.81 | 819211 | 0 | 0 | 0 |
