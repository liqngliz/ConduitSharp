# Microbenchmarks (Phase 1) — latest CI run

> Shared GitHub Actions runner: **Allocated (B/op) is deterministic and comparable;**
> **time columns are trend signal only.** Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29659383588

## BodyBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 2 logical cores and 1 physical core
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Mode       | BodyKB | Mean         | Error        | StdDev       | Gen0     | Gen1     | Allocated   |
|--------- |----------- |------- |-------------:|-------------:|-------------:|---------:|---------:|------------:|
| **PostBody** | **Auto**       | **1**      |     **90.58 μs** |    **215.55 μs** |    **11.815 μs** |   **2.1973** |        **-** |    **11.57 KB** |
| **PostBody** | **Auto**       | **1024**   |    **514.47 μs** |    **245.83 μs** |    **13.475 μs** |  **44.9219** |  **23.4375** |   **812.05 KB** |
| **PostBody** | **Auto**       | **10240**  |  **4,419.51 μs** |  **7,595.16 μs** |   **416.316 μs** | **500.0000** | **460.9375** | **10297.79 KB** |
| **PostBody** | **Buffered**   | **1**      |    **185.94 μs** |    **553.54 μs** |    **30.341 μs** |   **2.4414** |        **-** |    **12.69 KB** |
| **PostBody** | **Buffered**   | **1024**   |    **538.46 μs** |    **598.31 μs** |    **32.795 μs** |  **44.9219** |  **25.3906** |   **813.59 KB** |
| **PostBody** | **Buffered**   | **10240**  | **50,591.65 μs** | **33,385.24 μs** | **1,829.958 μs** | **437.5000** | **250.0000** | **10373.78 KB** |
| **PostBody** | **StreamOnly** | **1**      |     **75.00 μs** |     **82.51 μs** |     **4.523 μs** |   **2.1973** |        **-** |     **11.5 KB** |
| **PostBody** | **StreamOnly** | **1024**   |    **491.14 μs** |    **499.43 μs** |    **27.376 μs** |  **47.8516** |  **24.4141** |   **812.78 KB** |
| **PostBody** | **StreamOnly** | **10240**  |  **4,593.00 μs** | **11,875.27 μs** |   **650.924 μs** | **484.3750** | **453.1250** | **10298.77 KB** |

## GatewayBodyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 2 logical cores and 1 physical core
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Gateway            | BodyKB | Mean        | Error       | StdDev      | Gen0     | Gen1     | Gen2     | Allocated   |
|--------- |------------------- |------- |------------:|------------:|------------:|---------:|---------:|---------:|------------:|
| **PostBody** | **ConduitSharp**       | **1**      |    **368.5 μs** |    **389.6 μs** |    **21.35 μs** |   **2.9297** |        **-** |        **-** |    **16.07 KB** |
| **PostBody** | **ConduitSharp**       | **10240**  | **16,935.7 μs** | **15,199.6 μs** |   **833.14 μs** | **437.5000** | **375.0000** |        **-** | **10301.08 KB** |
| **PostBody** | **ConduitSharp-retry** | **1**      |    **300.9 μs** |    **578.2 μs** |    **31.69 μs** |   **3.4180** |        **-** |        **-** |    **17.34 KB** |
| **PostBody** | **ConduitSharp-retry** | **10240**  | **24,142.2 μs** | **32,328.9 μs** | **1,772.06 μs** | **437.5000** | **406.2500** |        **-** |  **10377.3 KB** |
| **PostBody** | **Ocelot**             | **1**      |    **461.2 μs** |    **588.7 μs** |    **32.27 μs** |   **5.8594** |        **-** |        **-** |     **29.2 KB** |
| **PostBody** | **Ocelot**             | **10240**  | **17,385.4 μs** |  **6,235.8 μs** |   **341.81 μs** | **406.2500** | **375.0000** |        **-** | **10315.02 KB** |
| **PostBody** | **Ocelot-retry**       | **1**      |    **550.6 μs** |    **447.1 μs** |    **24.50 μs** |   **7.8125** |        **-** |        **-** |    **41.76 KB** |
| **PostBody** | **Ocelot-retry**       | **10240**  | **19,988.0 μs** |  **8,574.7 μs** |   **470.01 μs** | **750.0000** | **718.7500** | **375.0000** | **20567.55 KB** |

## GatewayComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 2 logical cores and 1 physical core
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method     | Gateway      | RouteCount | Mean       | Error       | StdDev    | Gen0    | Allocated |
|----------- |------------- |----------- |-----------:|------------:|----------:|--------:|----------:|
| **ProxiedGet** | **ConduitSharp** | **1**          |   **242.5 μs** |   **251.50 μs** |  **13.79 μs** |  **2.4414** |   **14.4 KB** |
| **ProxiedGet** | **ConduitSharp** | **100**        |   **223.6 μs** |   **343.59 μs** |  **18.83 μs** |  **2.4414** |  **14.08 KB** |
| **ProxiedGet** | **ConduitSharp** | **500**        |   **242.9 μs** |    **35.41 μs** |   **1.94 μs** |  **2.4414** |  **14.13 KB** |
| **ProxiedGet** | **Ocelot**       | **1**          |   **411.7 μs** |   **505.35 μs** |  **27.70 μs** |  **4.8828** |  **26.22 KB** |
| **ProxiedGet** | **Ocelot**       | **100**        |   **466.3 μs** | **1,605.35 μs** |  **87.99 μs** |  **9.7656** |  **46.03 KB** |
| **ProxiedGet** | **Ocelot**       | **500**        | **1,095.5 μs** | **2,111.88 μs** | **115.76 μs** | **25.3906** | **127.03 KB** |

## GatewayPolicyComparisonBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 2 logical cores and 1 physical core
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method    | Gateway      | Mean     | Error    | StdDev   | Gen0   | Allocated |
|---------- |------------- |---------:|---------:|---------:|-------:|----------:|
| **AuthedGet** | **ConduitSharp** | **438.7 μs** | **201.6 μs** | **11.05 μs** | **3.9063** |  **21.19 KB** |
| **AuthedGet** | **Ocelot**       | **574.3 μs** | **532.5 μs** | **29.19 μs** | **6.8359** |  **37.65 KB** |

## JwtBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 2 logical cores and 1 physical core
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | RequiredClaims | Mean     | Error    | StdDev   | Gen0   | Allocated |
|--------- |--------------- |---------:|---------:|---------:|-------:|----------:|
| **Validate** | **False**          | **33.55 μs** | **3.579 μs** | **0.196 μs** | **1.7090** |   **8.95 KB** |
| **Validate** | **True**           | **35.70 μs** | **0.903 μs** | **0.049 μs** | **1.8311** |   **9.51 KB** |

## PluginPipelineBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 2 logical cores and 1 physical core
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method  | NoopPlugins | Mean     | Error     | StdDev    | Median   | Gen0   | Allocated |
|-------- |------------ |---------:|----------:|----------:|---------:|-------:|----------:|
| **Request** | **0**           | **36.58 μs** | **286.04 μs** | **15.679 μs** | **45.22 μs** | **1.5869** |   **8.22 KB** |
| **Request** | **1**           | **37.29 μs** |  **53.92 μs** |  **2.956 μs** | **37.68 μs** | **1.5869** |   **8.27 KB** |
| **Request** | **5**           | **33.32 μs** |  **64.62 μs** |  **3.542 μs** | **31.86 μs** | **1.5869** |   **8.45 KB** |

## RouteMatchBenchmarks

```

BenchmarkDotNet v0.15.4, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 2 logical cores and 1 physical core
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method         | RouteCount | Mean     | Error     | StdDev    | Gen0   | Allocated |
|--------------- |----------- |---------:|----------:|----------:|-------:|----------:|
| **MatchLastRoute** | **1**          | **33.66 μs** | **182.87 μs** | **10.024 μs** | **1.5869** |   **8.23 KB** |
| **MatchLastRoute** | **10**         | **37.65 μs** | **111.46 μs** |  **6.109 μs** | **1.5869** |   **8.23 KB** |
| **MatchLastRoute** | **100**        | **29.39 μs** | **155.85 μs** |  **8.542 μs** | **1.5869** |   **8.23 KB** |
| **MatchLastRoute** | **500**        | **33.89 μs** |  **44.77 μs** |  **2.454 μs** | **1.5869** |   **8.23 KB** |
