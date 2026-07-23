# Microbenchmarks (Phase 1) — latest CI run

> Shared GitHub Actions runner: **Allocated (B/op) is deterministic and comparable;**
> **time columns are trend signal only.** Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29957816337

## BodyBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Mode       | BodyKB | Mean         | Error        | StdDev    | Gen0     | Gen1     | Allocated   |
|--------- |----------- |------- |-------------:|-------------:|----------:|---------:|---------:|------------:|
| **PostBody** | **Auto**       | **1**      |     **62.74 μs** |    **436.70 μs** |  **23.94 μs** |   **0.9766** |        **-** |    **11.56 KB** |
| **PostBody** | **Auto**       | **1024**   |    **401.88 μs** |    **528.22 μs** |  **28.95 μs** |  **37.1094** |  **12.6953** |   **555.28 KB** |
| **PostBody** | **Auto**       | **10240**  |  **3,713.80 μs** |  **3,177.63 μs** | **174.18 μs** | **398.4375** | **320.3125** | **10040.92 KB** |
| **PostBody** | **Buffered**   | **1**      |    **172.76 μs** |    **444.76 μs** |  **24.38 μs** |   **0.9766** |        **-** |    **12.53 KB** |
| **PostBody** | **Buffered**   | **1024**   |    **427.57 μs** |  **1,153.96 μs** |  **63.25 μs** |  **37.1094** |  **12.6953** |   **554.58 KB** |
| **PostBody** | **Buffered**   | **10240**  | **25,364.28 μs** | **15,699.92 μs** | **860.57 μs** | **375.0000** | **312.5000** | **10115.36 KB** |
| **PostBody** | **StreamOnly** | **1**      |     **74.81 μs** |    **237.08 μs** |  **13.00 μs** |   **0.9766** |        **-** |    **11.57 KB** |
| **PostBody** | **StreamOnly** | **1024**   |    **411.25 μs** |    **201.47 μs** |  **11.04 μs** |  **36.1328** |  **11.7188** |   **555.21 KB** |
| **PostBody** | **StreamOnly** | **10240**  |  **3,467.49 μs** |  **2,749.14 μs** | **150.69 μs** | **414.0625** | **375.0000** | **10041.02 KB** |

## GatewayBodyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Gateway            | BodyKB | Mean        | Error       | StdDev      | Gen0     | Gen1     | Gen2     | Allocated   |
|--------- |------------------- |------- |------------:|------------:|------------:|---------:|---------:|---------:|------------:|
| **PostBody** | **ConduitSharp**       | **1**      |    **254.7 μs** |    **367.5 μs** |    **20.15 μs** |   **1.4648** |        **-** |        **-** |    **15.94 KB** |
| **PostBody** | **ConduitSharp**       | **10240**  | **15,482.2 μs** |  **7,758.6 μs** |   **425.27 μs** | **312.5000** | **281.2500** |        **-** |  **10044.2 KB** |
| **PostBody** | **ConduitSharp-retry** | **1**      |    **275.5 μs** |    **522.8 μs** |    **28.66 μs** |   **1.4648** |        **-** |        **-** |    **17.38 KB** |
| **PostBody** | **ConduitSharp-retry** | **10240**  | **22,897.9 μs** | **18,259.8 μs** | **1,000.88 μs** | **343.7500** | **281.2500** |        **-** | **10118.59 KB** |
| **PostBody** | **Ocelot**             | **1**      |    **426.6 μs** |    **823.5 μs** |    **45.14 μs** |   **2.9297** |        **-** |        **-** |    **29.23 KB** |
| **PostBody** | **Ocelot**             | **10240**  | **15,675.6 μs** |  **3,755.3 μs** |   **205.84 μs** | **343.7500** | **281.2500** |        **-** | **10057.96 KB** |
| **PostBody** | **Ocelot-retry**       | **1**      |    **478.7 μs** |    **735.7 μs** |    **40.33 μs** |   **3.9063** |        **-** |        **-** |     **41.5 KB** |
| **PostBody** | **Ocelot-retry**       | **10240**  | **18,742.8 μs** | **17,950.7 μs** |   **983.94 μs** | **625.0000** | **562.5000** | **312.5000** | **20310.52 KB** |

## GatewayComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method     | Gateway      | RouteCount | Mean     | Error    | StdDev   | Gen0    | Allocated |
|----------- |------------- |----------- |---------:|---------:|---------:|--------:|----------:|
| **ProxiedGet** | **ConduitSharp** | **1**          | **222.9 μs** | **345.4 μs** | **18.93 μs** |  **0.9766** |  **14.42 KB** |
| **ProxiedGet** | **ConduitSharp** | **100**        | **236.5 μs** | **349.9 μs** | **19.18 μs** |  **1.4648** |   **14.3 KB** |
| **ProxiedGet** | **ConduitSharp** | **500**        | **226.1 μs** | **271.2 μs** | **14.87 μs** |  **1.4648** |  **14.45 KB** |
| **ProxiedGet** | **Ocelot**       | **1**          | **400.7 μs** | **796.7 μs** | **43.67 μs** |  **1.9531** |  **25.96 KB** |
| **ProxiedGet** | **Ocelot**       | **100**        | **395.0 μs** | **878.3 μs** | **48.14 μs** |  **3.9063** |  **46.09 KB** |
| **ProxiedGet** | **Ocelot**       | **500**        | **455.6 μs** | **294.5 μs** | **16.14 μs** | **15.6250** | **158.25 KB** |

## GatewayPolicyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method    | Gateway      | Mean     | Error      | StdDev   | Gen0   | Allocated |
|---------- |------------- |---------:|-----------:|---------:|-------:|----------:|
| **AuthedGet** | **ConduitSharp** | **279.3 μs** |   **404.2 μs** | **22.16 μs** | **1.9531** |  **20.75 KB** |
| **AuthedGet** | **Ocelot**       | **491.1 μs** | **1,014.0 μs** | **55.58 μs** | **2.9297** |  **37.61 KB** |

## JwtBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | RequiredClaims | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------- |--------------- |---------:|---------:|---------:|-------:|----------:|
| **Validate** | **False**          | **32.89 μs** | **5.198 μs** | **0.285 μs** | **0.8545** |   **8.95 KB** |
| **Validate** | **True**           | **34.22 μs** | **2.291 μs** | **0.126 μs** | **0.8545** |   **9.51 KB** |

## PluginPipelineBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method  | NoopPlugins | Mean     | Error    | StdDev   | Gen0   | Allocated |
|-------- |------------ |---------:|---------:|---------:|-------:|----------:|
| **Request** | **0**           | **19.41 μs** | **50.95 μs** | **2.793 μs** | **0.8545** |   **8.22 KB** |
| **Request** | **1**           | **21.13 μs** | **29.79 μs** | **1.633 μs** | **0.8545** |   **8.27 KB** |
| **Request** | **5**           | **21.35 μs** | **22.77 μs** | **1.248 μs** | **0.8545** |   **8.45 KB** |

## RouteMatchBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method         | RouteCount | Mean     | Error     | StdDev   | Gen0   | Allocated |
|--------------- |----------- |---------:|----------:|---------:|-------:|----------:|
| **MatchLastRoute** | **1**          | **20.74 μs** |  **46.95 μs** | **2.574 μs** | **0.8545** |   **8.26 KB** |
| **MatchLastRoute** | **10**         | **68.12 μs** | **116.04 μs** | **6.361 μs** | **0.7324** |   **8.36 KB** |
| **MatchLastRoute** | **100**        | **20.20 μs** |  **33.50 μs** | **1.836 μs** | **0.8545** |   **8.23 KB** |
| **MatchLastRoute** | **500**        | **26.02 μs** | **117.37 μs** | **6.433 μs** | **0.8545** |   **8.23 KB** |
