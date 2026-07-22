# Microbenchmarks (Phase 1) — latest CI run

> Shared GitHub Actions runner: **Allocated (B/op) is deterministic and comparable;**
> **time columns are trend signal only.** Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29898589358

## BodyBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.03GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Mode       | BodyKB | Mean         | Error       | StdDev     | Gen0     | Gen1     | Allocated   |
|--------- |----------- |------- |-------------:|------------:|-----------:|---------:|---------:|------------:|
| **PostBody** | **Auto**       | **1**      |    **133.34 μs** |   **373.92 μs** |  **20.496 μs** |   **0.9766** |        **-** |    **11.76 KB** |
| **PostBody** | **Auto**       | **1024**   |    **430.05 μs** |   **722.87 μs** |  **39.623 μs** |  **36.1328** |  **12.6953** |   **555.21 KB** |
| **PostBody** | **Auto**       | **10240**  |  **3,924.21 μs** | **1,887.96 μs** | **103.485 μs** | **390.6250** | **320.3125** | **10041.02 KB** |
| **PostBody** | **Buffered**   | **1**      |    **164.17 μs** |   **325.86 μs** |  **17.861 μs** |   **0.9766** |        **-** |    **12.54 KB** |
| **PostBody** | **Buffered**   | **1024**   |    **452.11 μs** |   **567.59 μs** |  **31.111 μs** |  **34.1797** |  **11.7188** |   **554.66 KB** |
| **PostBody** | **Buffered**   | **10240**  | **25,378.40 μs** | **8,734.22 μs** | **478.752 μs** | **375.0000** | **187.5000** | **10115.74 KB** |
| **PostBody** | **StreamOnly** | **1**      |     **65.02 μs** |   **280.51 μs** |  **15.376 μs** |   **0.9766** |        **-** |     **11.6 KB** |
| **PostBody** | **StreamOnly** | **1024**   |    **418.16 μs** |    **81.52 μs** |   **4.469 μs** |  **32.2266** |  **12.6953** |   **555.17 KB** |
| **PostBody** | **StreamOnly** | **10240**  |  **3,911.27 μs** | **2,469.84 μs** | **135.380 μs** | **414.0625** | **390.6250** | **10041.22 KB** |

## GatewayBodyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.03GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Gateway            | BodyKB | Mean        | Error       | StdDev    | Gen0     | Gen1     | Gen2     | Allocated   |
|--------- |------------------- |------- |------------:|------------:|----------:|---------:|---------:|---------:|------------:|
| **PostBody** | **ConduitSharp**       | **1**      |    **248.6 μs** |    **349.8 μs** |  **19.18 μs** |   **1.4648** |        **-** |        **-** |    **16.01 KB** |
| **PostBody** | **ConduitSharp**       | **10240**  | **15,933.5 μs** | **13,202.0 μs** | **723.65 μs** | **343.7500** | **312.5000** |        **-** | **10043.69 KB** |
| **PostBody** | **ConduitSharp-retry** | **1**      |    **407.8 μs** |    **372.7 μs** |  **20.43 μs** |   **0.9766** |        **-** |        **-** |    **17.66 KB** |
| **PostBody** | **ConduitSharp-retry** | **10240**  | **23,346.3 μs** | **12,523.8 μs** | **686.47 μs** | **312.5000** | **187.5000** |        **-** | **10118.78 KB** |
| **PostBody** | **Ocelot**             | **1**      |    **420.7 μs** |    **374.3 μs** |  **20.52 μs** |   **1.9531** |        **-** |        **-** |    **29.19 KB** |
| **PostBody** | **Ocelot**             | **10240**  | **16,785.0 μs** | **11,937.9 μs** | **654.36 μs** | **375.0000** | **343.7500** |        **-** | **10056.69 KB** |
| **PostBody** | **Ocelot-retry**       | **1**      |    **490.8 μs** |    **674.5 μs** |  **36.97 μs** |   **3.9063** |        **-** |        **-** |    **41.44 KB** |
| **PostBody** | **Ocelot-retry**       | **10240**  | **19,207.3 μs** | **14,671.1 μs** | **804.17 μs** | **625.0000** | **593.7500** | **312.5000** | **20310.83 KB** |

## GatewayComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.03GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method     | Gateway      | RouteCount | Mean     | Error    | StdDev   | Gen0    | Allocated |
|----------- |------------- |----------- |---------:|---------:|---------:|--------:|----------:|
| **ProxiedGet** | **ConduitSharp** | **1**          | **231.4 μs** | **603.5 μs** | **33.08 μs** |  **1.4648** |   **14.4 KB** |
| **ProxiedGet** | **ConduitSharp** | **100**        | **223.4 μs** | **389.1 μs** | **21.33 μs** |  **0.9766** |  **14.49 KB** |
| **ProxiedGet** | **ConduitSharp** | **500**        | **242.2 μs** | **308.6 μs** | **16.92 μs** |  **1.4648** |  **14.41 KB** |
| **ProxiedGet** | **Ocelot**       | **1**          | **374.6 μs** | **592.0 μs** | **32.45 μs** |  **1.9531** |  **25.96 KB** |
| **ProxiedGet** | **Ocelot**       | **100**        | **376.8 μs** | **624.6 μs** | **34.23 μs** |  **3.9063** |  **45.92 KB** |
| **ProxiedGet** | **Ocelot**       | **500**        | **469.1 μs** | **193.7 μs** | **10.62 μs** | **16.6016** | **158.27 KB** |

## GatewayPolicyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.03GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method    | Gateway      | Mean     | Error    | StdDev   | Gen0   | Allocated |
|---------- |------------- |---------:|---------:|---------:|-------:|----------:|
| **AuthedGet** | **ConduitSharp** | **452.5 μs** | **419.6 μs** | **23.00 μs** | **1.9531** |  **20.92 KB** |
| **AuthedGet** | **Ocelot**       | **489.4 μs** | **828.0 μs** | **45.38 μs** | **2.9297** |  **37.54 KB** |

## JwtBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.03GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | RequiredClaims | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------- |--------------- |---------:|---------:|---------:|-------:|----------:|
| **Validate** | **False**          | **33.37 μs** | **2.427 μs** | **0.133 μs** | **0.8545** |   **8.95 KB** |
| **Validate** | **True**           | **35.55 μs** | **2.596 μs** | **0.142 μs** | **0.8545** |   **9.51 KB** |

## PluginPipelineBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.03GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method  | NoopPlugins | Mean     | Error     | StdDev    | Gen0   | Allocated |
|-------- |------------ |---------:|----------:|----------:|-------:|----------:|
| **Request** | **0**           | **21.72 μs** |  **13.11 μs** |  **0.719 μs** | **0.8545** |   **8.24 KB** |
| **Request** | **1**           | **55.82 μs** | **320.18 μs** | **17.550 μs** | **0.7324** |   **8.28 KB** |
| **Request** | **5**           | **58.70 μs** | **369.22 μs** | **20.238 μs** | **0.7324** |   **8.48 KB** |

## RouteMatchBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.03GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method         | RouteCount | Mean     | Error     | StdDev   | Gen0   | Allocated |
|--------------- |----------- |---------:|----------:|---------:|-------:|----------:|
| **MatchLastRoute** | **1**          | **23.28 μs** |  **98.76 μs** | **5.413 μs** | **0.8545** |   **8.23 KB** |
| **MatchLastRoute** | **10**         | **27.12 μs** | **134.79 μs** | **7.388 μs** | **0.7324** |   **8.23 KB** |
| **MatchLastRoute** | **100**        | **62.91 μs** | **130.80 μs** | **7.170 μs** | **0.7324** |   **8.28 KB** |
| **MatchLastRoute** | **500**        | **18.88 μs** |  **26.33 μs** | **1.443 μs** | **0.8545** |   **8.23 KB** |
