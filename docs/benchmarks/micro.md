# Microbenchmarks (Phase 1) — latest CI run

> Shared GitHub Actions runner: **Allocated (B/op) is deterministic and comparable;**
> **time columns are trend signal only.** Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29905486090

## BodyBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Mode       | BodyKB | Mean         | Error        | StdDev     | Gen0     | Gen1     | Allocated   |
|--------- |----------- |------- |-------------:|-------------:|-----------:|---------:|---------:|------------:|
| **PostBody** | **Auto**       | **1**      |     **73.53 μs** |    **152.22 μs** |   **8.344 μs** |   **0.9766** |        **-** |    **11.56 KB** |
| **PostBody** | **Auto**       | **1024**   |    **367.72 μs** |    **294.51 μs** |  **16.143 μs** |  **36.1328** |  **13.6719** |   **554.96 KB** |
| **PostBody** | **Auto**       | **10240**  |  **2,492.10 μs** |  **2,089.62 μs** | **114.539 μs** | **398.4375** | **363.2813** |  **10040.7 KB** |
| **PostBody** | **Buffered**   | **1**      |     **78.32 μs** |    **431.38 μs** |  **23.645 μs** |   **1.2207** |        **-** |    **12.13 KB** |
| **PostBody** | **Buffered**   | **1024**   |    **463.59 μs** |  **1,011.16 μs** |  **55.425 μs** |  **36.1328** |  **11.7188** |   **553.62 KB** |
| **PostBody** | **Buffered**   | **10240**  | **62,000.83 μs** | **10,759.57 μs** | **589.768 μs** | **375.0000** | **125.0000** | **10116.26 KB** |
| **PostBody** | **StreamOnly** | **1**      |     **66.09 μs** |    **243.59 μs** |  **13.352 μs** |   **0.9766** |        **-** |    **11.56 KB** |
| **PostBody** | **StreamOnly** | **1024**   |    **424.30 μs** |    **339.68 μs** |  **18.619 μs** |  **35.1563** |  **12.6953** |   **554.86 KB** |
| **PostBody** | **StreamOnly** | **10240**  |  **2,656.45 μs** |  **1,284.17 μs** |  **70.390 μs** | **402.3438** | **363.2813** | **10041.38 KB** |

## GatewayBodyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Gateway            | BodyKB | Mean            | Error       | StdDev    | Gen0     | Gen1     | Gen2     | Allocated   |
|--------- |------------------- |------- |----------------:|------------:|----------:|---------:|---------:|---------:|------------:|
| **PostBody** | **ConduitSharp**       | **1**      |        **212.3 μs** |    **402.5 μs** |  **22.06 μs** |   **1.4648** |        **-** |        **-** |    **15.93 KB** |
| **PostBody** | **ConduitSharp**       | **10240**  |     **11,665.4 μs** |  **6,639.3 μs** | **363.92 μs** | **343.7500** | **296.8750** |        **-** | **10043.85 KB** |
| **PostBody** | **ConduitSharp-retry** | **1**      |        **226.1 μs** |    **693.4 μs** |  **38.01 μs** |   **1.4648** |        **-** |        **-** |    **17.31 KB** |
| **PostBody** | **ConduitSharp-retry** | **10240**  | **70,982,521.2 μs** | **17,831.0 μs** | **977.38 μs** | **343.7500** | **281.2500** |        **-** | **10257.19 KB** |
| **PostBody** | **Ocelot**             | **1**      |        **199.5 μs** |    **188.4 μs** |  **10.33 μs** |   **2.9297** |        **-** |        **-** |     **28.9 KB** |
| **PostBody** | **Ocelot**             | **10240**  |     **11,550.9 μs** |  **9,959.3 μs** | **545.90 μs** | **312.5000** | **265.6250** |        **-** | **10056.77 KB** |
| **PostBody** | **Ocelot-retry**       | **1**      |        **438.3 μs** |  **1,410.1 μs** |  **77.29 μs** |   **3.9063** |        **-** |        **-** |    **41.39 KB** |
| **PostBody** | **Ocelot-retry**       | **10240**  |     **17,053.2 μs** | **16,950.9 μs** | **929.14 μs** | **625.0000** | **531.2500** | **312.5000** | **20309.47 KB** |

## GatewayComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method     | Gateway      | RouteCount | Mean     | Error     | StdDev   | Gen0    | Allocated |
|----------- |------------- |----------- |---------:|----------:|---------:|--------:|----------:|
| **ProxiedGet** | **ConduitSharp** | **1**          | **170.6 μs** | **391.41 μs** | **21.45 μs** |  **0.9766** |  **14.18 KB** |
| **ProxiedGet** | **ConduitSharp** | **100**        | **170.2 μs** | **424.39 μs** | **23.26 μs** |  **1.4648** |  **14.17 KB** |
| **ProxiedGet** | **ConduitSharp** | **500**        | **186.2 μs** | **414.77 μs** | **22.73 μs** |  **0.9766** |  **14.09 KB** |
| **ProxiedGet** | **Ocelot**       | **1**          | **184.5 μs** | **342.80 μs** | **18.79 μs** |  **2.4414** |  **25.55 KB** |
| **ProxiedGet** | **Ocelot**       | **100**        | **354.2 μs** | **917.95 μs** | **50.32 μs** |  **3.9063** |  **45.95 KB** |
| **ProxiedGet** | **Ocelot**       | **500**        | **422.3 μs** |  **49.43 μs** |  **2.71 μs** | **15.6250** | **158.12 KB** |

## GatewayPolicyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method    | Gateway      | Mean     | Error    | StdDev   | Gen0   | Allocated |
|---------- |------------- |---------:|---------:|---------:|-------:|----------:|
| **AuthedGet** | **ConduitSharp** | **256.8 μs** | **699.4 μs** | **38.33 μs** | **1.9531** |   **20.8 KB** |
| **AuthedGet** | **Ocelot**       | **480.0 μs** | **383.0 μs** | **20.99 μs** | **2.9297** |  **37.57 KB** |

## JwtBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | RequiredClaims | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------- |--------------- |---------:|---------:|---------:|-------:|----------:|
| **Validate** | **False**          | **25.91 μs** | **4.062 μs** | **0.223 μs** | **0.8545** |   **8.95 KB** |
| **Validate** | **True**           | **27.71 μs** | **3.166 μs** | **0.174 μs** | **0.9155** |   **9.51 KB** |

## PluginPipelineBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method  | NoopPlugins | Mean     | Error     | StdDev    | Gen0   | Allocated |
|-------- |------------ |---------:|----------:|----------:|-------:|----------:|
| **Request** | **0**           | **32.10 μs** | **207.83 μs** | **11.392 μs** | **0.7324** |   **8.26 KB** |
| **Request** | **1**           | **33.38 μs** | **130.54 μs** |  **7.155 μs** | **0.8545** |   **8.35 KB** |
| **Request** | **5**           | **27.41 μs** | **106.05 μs** |  **5.813 μs** | **0.8545** |   **8.49 KB** |

## RouteMatchBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method         | RouteCount | Mean     | Error     | StdDev    | Gen0   | Allocated |
|--------------- |----------- |---------:|----------:|----------:|-------:|----------:|
| **MatchLastRoute** | **1**          | **30.88 μs** | **286.98 μs** | **15.730 μs** | **0.8545** |   **8.26 KB** |
| **MatchLastRoute** | **10**         | **32.19 μs** | **137.85 μs** |  **7.556 μs** | **0.8545** |   **8.25 KB** |
| **MatchLastRoute** | **100**        | **30.54 μs** | **145.78 μs** |  **7.991 μs** | **0.8545** |   **8.26 KB** |
| **MatchLastRoute** | **500**        | **28.08 μs** | **104.59 μs** |  **5.733 μs** | **0.8545** |   **8.23 KB** |
