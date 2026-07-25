# Microbenchmarks (Phase 1) — latest CI run

> Shared GitHub Actions runner: **Allocated (B/op) is deterministic and comparable;**
> **time columns are trend signal only.** Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/30158839718

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
| Method   | Mode       | BodyKB | Mean         | Error       | StdDev    | Gen0     | Gen1     | Allocated   |
|--------- |----------- |------- |-------------:|------------:|----------:|---------:|---------:|------------:|
| **PostBody** | **Auto**       | **1**      |     **57.82 μs** |   **215.81 μs** |  **11.83 μs** |   **0.9766** |        **-** |    **11.56 KB** |
| **PostBody** | **Auto**       | **1024**   |    **415.78 μs** |   **327.80 μs** |  **17.97 μs** |  **36.1328** |  **13.6719** |   **555.26 KB** |
| **PostBody** | **Auto**       | **10240**  |  **3,639.32 μs** | **2,662.27 μs** | **145.93 μs** | **390.6250** | **328.1250** |  **10040.8 KB** |
| **PostBody** | **Buffered**   | **1**      |    **145.47 μs** |   **200.96 μs** |  **11.02 μs** |   **0.9766** |        **-** |     **12.4 KB** |
| **PostBody** | **Buffered**   | **1024**   |    **480.84 μs** | **1,055.26 μs** |  **57.84 μs** |  **36.1328** |  **12.6953** |   **554.57 KB** |
| **PostBody** | **Buffered**   | **10240**  | **25,305.86 μs** | **7,394.40 μs** | **405.31 μs** | **375.0000** | **343.7500** | **10115.61 KB** |
| **PostBody** | **StreamOnly** | **1**      |     **70.32 μs** |   **362.99 μs** |  **19.90 μs** |   **0.9766** |        **-** |    **11.61 KB** |
| **PostBody** | **StreamOnly** | **1024**   |    **416.50 μs** |   **323.33 μs** |  **17.72 μs** |  **36.1328** |  **13.6719** |    **555.6 KB** |
| **PostBody** | **StreamOnly** | **10240**  |  **3,872.24 μs** | **3,000.58 μs** | **164.47 μs** | **390.6250** | **335.9375** | **10040.88 KB** |

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
| **PostBody** | **ConduitSharp**       | **1**      |    **255.1 μs** |    **425.7 μs** |    **23.33 μs** |   **1.4648** |        **-** |        **-** |    **16.33 KB** |
| **PostBody** | **ConduitSharp**       | **10240**  | **15,205.2 μs** | **13,224.6 μs** |   **724.88 μs** | **343.7500** | **281.2500** |        **-** | **10044.34 KB** |
| **PostBody** | **ConduitSharp-retry** | **1**      |    **277.5 μs** |    **440.8 μs** |    **24.16 μs** |   **1.4648** |        **-** |        **-** |    **17.76 KB** |
| **PostBody** | **ConduitSharp-retry** | **10240**  | **24,004.9 μs** | **20,983.8 μs** | **1,150.19 μs** | **343.7500** | **281.2500** |        **-** | **10119.45 KB** |
| **PostBody** | **Ocelot**             | **1**      |    **456.4 μs** |    **257.4 μs** |    **14.11 μs** |   **2.9297** |        **-** |        **-** |    **29.44 KB** |
| **PostBody** | **Ocelot**             | **10240**  | **16,891.8 μs** | **13,172.9 μs** |   **722.05 μs** | **312.5000** | **250.0000** |        **-** | **10057.07 KB** |
| **PostBody** | **Ocelot-retry**       | **1**      |    **481.3 μs** |  **1,108.2 μs** |    **60.75 μs** |   **3.9063** |        **-** |        **-** |     **41.4 KB** |
| **PostBody** | **Ocelot-retry**       | **10240**  | **19,396.7 μs** | **15,024.4 μs** |   **823.54 μs** | **625.0000** | **593.7500** | **312.5000** | **20310.64 KB** |

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
| **ProxiedGet** | **ConduitSharp** | **1**          | **212.0 μs** |   **289.8 μs** | **15.89 μs** |  **0.9766** |   **14.3 KB** |
| **ProxiedGet** | **ConduitSharp** | **100**        | **236.7 μs** |   **397.7 μs** | **21.80 μs** |  **1.4648** |   **14.3 KB** |
| **ProxiedGet** | **ConduitSharp** | **500**        | **223.5 μs** |   **447.0 μs** | **24.50 μs** |  **1.4648** |   **14.5 KB** |
| **ProxiedGet** | **Ocelot**       | **1**          | **353.8 μs** |   **647.7 μs** | **35.50 μs** |  **1.9531** |  **26.01 KB** |
| **ProxiedGet** | **Ocelot**       | **100**        | **393.3 μs** | **1,145.3 μs** | **62.78 μs** |  **3.9063** |  **46.44 KB** |
| **ProxiedGet** | **Ocelot**       | **500**        | **463.8 μs** |   **301.9 μs** | **16.55 μs** | **15.6250** | **158.16 KB** |

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
| Method    | Gateway      | Mean     | Error      | StdDev   | Gen0   | Allocated |
|---------- |------------- |---------:|-----------:|---------:|-------:|----------:|
| **AuthedGet** | **ConduitSharp** | **282.6 μs** |   **447.0 μs** | **24.50 μs** | **1.9531** |  **20.96 KB** |
| **AuthedGet** | **Ocelot**       | **520.1 μs** | **1,255.1 μs** | **68.79 μs** | **2.9297** |  **37.63 KB** |

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
| **Validate** | **False**          | **33.55 μs** | **6.561 μs** | **0.360 μs** | **0.8545** |   **8.95 KB** |
| **Validate** | **True**           | **34.86 μs** | **3.969 μs** | **0.218 μs** | **0.8545** |   **9.51 KB** |

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
| Method  | NoopPlugins | Mean     | Error    | StdDev   | Gen0   | Allocated |
|-------- |------------ |---------:|---------:|---------:|-------:|----------:|
| **Request** | **0**           | **21.00 μs** | **24.86 μs** | **1.362 μs** | **0.7324** |   **8.22 KB** |
| **Request** | **1**           | **22.10 μs** | **20.98 μs** | **1.150 μs** | **0.8545** |   **8.27 KB** |
| **Request** | **5**           | **22.02 μs** | **92.74 μs** | **5.084 μs** | **0.8545** |   **8.46 KB** |

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
| Method         | RouteCount | Mean     | Error     | StdDev    | Gen0   | Allocated |
|--------------- |----------- |---------:|----------:|----------:|-------:|----------:|
| **MatchLastRoute** | **1**          | **19.71 μs** |  **22.98 μs** |  **1.260 μs** | **0.8545** |   **8.24 KB** |
| **MatchLastRoute** | **10**         | **57.41 μs** | **218.98 μs** | **12.003 μs** | **0.7324** |   **8.24 KB** |
| **MatchLastRoute** | **100**        | **24.84 μs** | **143.41 μs** |  **7.861 μs** | **0.8545** |   **8.23 KB** |
| **MatchLastRoute** | **500**        | **18.53 μs** |  **46.96 μs** |  **2.574 μs** | **0.7324** |   **8.25 KB** |
