# Latest CI benchmark run (raw figures)

> Shared GitHub Actions runner (4 vCPU): absolute numbers are run-to-run noise;
> the published claim is the README's same-rig ratio table.
> Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29905486090


## 2026-07-22T08:49:30Z — DUR=60s CONNS=125 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| direct-to-upstream (no gateway) [c=125] | 100056 | 1.25 | 1.26 | 3.43 | 6003471 | 0 | 0 | 0 |
| scenario-a pure proxy (max QPS) [c=125] | 24422 | 5.12 | 4.32 | 18.38 | 1464647 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=125] | 16535 | 7.56 | 6.76 | 23.02 | 991830 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=125] | 25023 | 4.99 | 2.73 | 23.70 | 1501403 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=125] | 19990 | 6.25 | 5.96 | 12.33 | 1199368 | 0 | 0 | 0 |

## 2026-07-22T09:00:10Z — DUR=60s CONNS=512 RATE=0 PIN=0 host=Linux x86_64

| run | QPS (mean) | lat mean ms | p50 ms | p99 ms | 2xx | 4xx | 5xx | conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| scenario-a pure proxy (max QPS) [c=512] | 24668 | 20.93 | 18.08 | 64.43 | 1467912 | 0 | 0 | 0 |
| ocelot pure proxy (max QPS) [c=512] | 17282 | 30.07 | 27.39 | 80.31 | 1021536 | 0 | 0 | 0 |
| apisix pure proxy (max QPS) [c=512] | 24904 | 20.57 | 18.09 | 64.09 | 1493423 | 0 | 0 | 0 |
| envoy pure proxy (max QPS) [c=512] | 19406 | 26.38 | 25.89 | 45.06 | 1164360 | 0 | 0 | 0 |
