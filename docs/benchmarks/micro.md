# Microbenchmarks (Phase 1) — latest CI run

> Shared GitHub Actions runner: **Allocated (B/op) is deterministic and comparable;**
> **time columns are trend signal only.** Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/30165314699

## BodyBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Mode       | BodyKB | Mean         | Error        | StdDev    | Gen0     | Gen1     | Allocated   |
|--------- |----------- |------- |-------------:|-------------:|----------:|---------:|---------:|------------:|
| **PostBody** | **Auto**       | **1**      |     **65.10 μs** |    **242.48 μs** |  **13.29 μs** |   **0.9766** |        **-** |    **11.57 KB** |
| **PostBody** | **Auto**       | **1024**   |    **408.83 μs** |    **516.92 μs** |  **28.33 μs** |  **37.1094** |  **10.7422** |   **555.51 KB** |
| **PostBody** | **Auto**       | **10240**  |  **3,471.98 μs** |  **1,564.78 μs** |  **85.77 μs** | **390.6250** | **351.5625** | **10041.24 KB** |
| **PostBody** | **Buffered**   | **1**      |    **172.77 μs** |    **535.22 μs** |  **29.34 μs** |   **0.9766** |        **-** |    **12.36 KB** |
| **PostBody** | **Buffered**   | **1024**   |    **425.82 μs** |    **633.33 μs** |  **34.72 μs** |  **34.1797** |  **10.7422** |    **554.7 KB** |
| **PostBody** | **Buffered**   | **10240**  | **25,426.13 μs** | **12,382.44 μs** | **678.72 μs** | **406.2500** | **312.5000** | **10114.99 KB** |
| **PostBody** | **StreamOnly** | **1**      |     **66.47 μs** |    **319.49 μs** |  **17.51 μs** |   **0.9766** |        **-** |    **11.57 KB** |
| **PostBody** | **StreamOnly** | **1024**   |    **425.05 μs** |    **750.12 μs** |  **41.12 μs** |  **38.0859** |  **12.6953** |   **555.01 KB** |
| **PostBody** | **StreamOnly** | **10240**  |  **3,380.22 μs** |  **3,958.48 μs** | **216.98 μs** | **390.6250** | **328.1250** | **10041.43 KB** |

## GatewayBodyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Gateway            | BodyKB | Mean        | Error       | StdDev      | Gen0     | Gen1     | Gen2     | Allocated   |
|--------- |------------------- |------- |------------:|------------:|------------:|---------:|---------:|---------:|------------:|
| **PostBody** | **ConduitSharp**       | **1**      |    **356.6 μs** |    **228.2 μs** |    **12.51 μs** |   **0.9766** |        **-** |        **-** |    **16.08 KB** |
| **PostBody** | **ConduitSharp**       | **10240**  | **15,659.0 μs** |  **4,789.6 μs** |   **262.54 μs** | **343.7500** | **281.2500** |        **-** | **10043.79 KB** |
| **PostBody** | **ConduitSharp-retry** | **1**      |    **268.6 μs** |    **450.9 μs** |    **24.72 μs** |   **1.4648** |        **-** |        **-** |    **17.86 KB** |
| **PostBody** | **ConduitSharp-retry** | **10240**  | **23,840.6 μs** | **25,545.6 μs** | **1,400.24 μs** | **312.5000** | **281.2500** |        **-** | **10118.84 KB** |
| **PostBody** | **Ocelot**             | **1**      |    **427.5 μs** |    **223.3 μs** |    **12.24 μs** |   **2.9297** |        **-** |        **-** |    **29.26 KB** |
| **PostBody** | **Ocelot**             | **10240**  | **15,299.4 μs** |  **4,404.7 μs** |   **241.44 μs** | **312.5000** | **250.0000** |        **-** |    **10062 KB** |
| **PostBody** | **Ocelot-retry**       | **1**      |    **477.8 μs** |  **1,229.1 μs** |    **67.37 μs** |   **3.9063** |        **-** |        **-** |    **41.42 KB** |
| **PostBody** | **Ocelot-retry**       | **10240**  | **18,529.8 μs** | **14,757.8 μs** |   **808.92 μs** | **625.0000** | **531.2500** | **312.5000** | **20311.21 KB** |

## GatewayComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method     | Gateway      | RouteCount | Mean     | Error      | StdDev   | Gen0    | Allocated |
|----------- |------------- |----------- |---------:|-----------:|---------:|--------:|----------:|
| **ProxiedGet** | **ConduitSharp** | **1**          | **234.0 μs** |   **152.7 μs** |  **8.37 μs** |  **1.4648** |  **14.34 KB** |
| **ProxiedGet** | **ConduitSharp** | **100**        | **222.6 μs** |   **267.4 μs** | **14.66 μs** |  **1.4648** |  **14.38 KB** |
| **ProxiedGet** | **ConduitSharp** | **500**        | **218.0 μs** |   **491.3 μs** | **26.93 μs** |  **1.4648** |  **14.28 KB** |
| **ProxiedGet** | **Ocelot**       | **1**          | **366.8 μs** |   **707.0 μs** | **38.75 μs** |  **1.9531** |  **25.92 KB** |
| **ProxiedGet** | **Ocelot**       | **100**        | **408.4 μs** | **1,218.8 μs** | **66.81 μs** |  **3.9063** |  **45.98 KB** |
| **ProxiedGet** | **Ocelot**       | **500**        | **463.4 μs** |   **248.9 μs** | **13.64 μs** | **15.6250** | **158.21 KB** |

## GatewayPolicyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method    | Gateway      | Mean     | Error    | StdDev   | Gen0   | Allocated |
|---------- |------------- |---------:|---------:|---------:|-------:|----------:|
| **AuthedGet** | **ConduitSharp** | **286.2 μs** | **457.1 μs** | **25.05 μs** | **1.9531** |  **21.01 KB** |
| **AuthedGet** | **Ocelot**       | **498.7 μs** | **658.0 μs** | **36.07 μs** | **2.9297** |  **37.62 KB** |

## JwtBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | RequiredClaims | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------- |--------------- |---------:|---------:|---------:|-------:|----------:|
| **Validate** | **False**          | **33.12 μs** | **5.912 μs** | **0.324 μs** | **0.8545** |   **8.95 KB** |
| **Validate** | **True**           | **33.82 μs** | **3.133 μs** | **0.172 μs** | **0.8545** |   **9.51 KB** |

## PluginPipelineBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method  | NoopPlugins | Mean     | Error     | StdDev   | Gen0   | Allocated |
|-------- |------------ |---------:|----------:|---------:|-------:|----------:|
| **Request** | **0**           | **20.12 μs** |  **25.22 μs** | **1.382 μs** | **0.8545** |   **8.22 KB** |
| **Request** | **1**           | **24.82 μs** | **103.71 μs** | **5.685 μs** | **0.8545** |   **8.27 KB** |
| **Request** | **5**           | **26.99 μs** |  **73.28 μs** | **4.017 μs** | **0.8545** |   **8.45 KB** |

## RouteMatchBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method         | RouteCount | Mean     | Error     | StdDev   | Gen0   | Allocated |
|--------------- |----------- |---------:|----------:|---------:|-------:|----------:|
| **MatchLastRoute** | **1**          | **22.90 μs** |  **15.64 μs** | **0.857 μs** | **0.8545** |   **8.23 KB** |
| **MatchLastRoute** | **10**         | **20.03 μs** |  **23.61 μs** | **1.294 μs** | **0.8545** |   **8.23 KB** |
| **MatchLastRoute** | **100**        | **25.88 μs** | **103.64 μs** | **5.681 μs** | **0.8545** |   **8.23 KB** |
| **MatchLastRoute** | **500**        | **17.89 μs** |  **33.56 μs** | **1.840 μs** | **0.8545** |   **8.23 KB** |
