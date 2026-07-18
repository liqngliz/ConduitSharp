# Microbenchmarks (Phase 1) — latest CI run

> Shared GitHub Actions runner: **Allocated (B/op) is deterministic and comparable;**
> **time columns are trend signal only.** Source run: https://github.com/liqngliz/ConduitSharp/actions/runs/29594008284

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
| Method   | Mode       | BodyKB | Mean         | Error        | StdDev      | Gen0     | Gen1     | Allocated   |
|--------- |----------- |------- |-------------:|-------------:|------------:|---------:|---------:|------------:|
| **PostBody** | **Auto**       | **1**      |     **69.99 μs** |    **373.92 μs** |    **20.50 μs** |   **2.1973** |        **-** |     **11.5 KB** |
| **PostBody** | **Auto**       | **1024**   |    **499.73 μs** |    **391.09 μs** |    **21.44 μs** |  **47.8516** |  **25.3906** |   **811.66 KB** |
| **PostBody** | **Auto**       | **10240**  |  **4,225.71 μs** |  **2,596.73 μs** |   **142.34 μs** | **453.1250** | **429.6875** | **10297.82 KB** |
| **PostBody** | **Buffered**   | **1**      |    **173.88 μs** |    **610.59 μs** |    **33.47 μs** |   **2.4414** |        **-** |     **12.6 KB** |
| **PostBody** | **Buffered**   | **1024**   |    **538.25 μs** |  **1,173.79 μs** |    **64.34 μs** |  **51.7578** |  **26.3672** |   **813.27 KB** |
| **PostBody** | **Buffered**   | **10240**  | **50,750.04 μs** | **31,673.84 μs** | **1,736.15 μs** | **437.5000** | **375.0000** | **10373.99 KB** |
| **PostBody** | **StreamOnly** | **1**      |     **65.53 μs** |    **319.75 μs** |    **17.53 μs** |   **2.1973** |        **-** |     **11.5 KB** |
| **PostBody** | **StreamOnly** | **1024**   |    **520.74 μs** |    **353.54 μs** |    **19.38 μs** |  **46.8750** |  **18.5547** |   **813.65 KB** |
| **PostBody** | **StreamOnly** | **10240**  |  **4,397.28 μs** |  **3,884.77 μs** |   **212.94 μs** | **453.1250** | **414.0625** | **10297.77 KB** |

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
| Method   | Gateway            | BodyKB | Mean        | Error        | StdDev      | Gen0     | Gen1     | Gen2     | Allocated   |
|--------- |------------------- |------- |------------:|-------------:|------------:|---------:|---------:|---------:|------------:|
| **PostBody** | **ConduitSharp**       | **1**      |    **271.3 μs** |     **44.21 μs** |     **2.42 μs** |   **2.9297** |        **-** |        **-** |    **15.85 KB** |
| **PostBody** | **ConduitSharp**       | **10240**  | **17,018.5 μs** | **23,317.45 μs** | **1,278.11 μs** | **437.5000** | **406.2500** |        **-** | **10301.89 KB** |
| **PostBody** | **ConduitSharp-retry** | **1**      |    **286.6 μs** |    **206.56 μs** |    **11.32 μs** |   **3.4180** |        **-** |        **-** |    **17.47 KB** |
| **PostBody** | **ConduitSharp-retry** | **10240**  | **24,741.6 μs** | **38,007.83 μs** | **2,083.34 μs** | **375.0000** | **312.5000** |        **-** | **10376.04 KB** |
| **PostBody** | **Ocelot**             | **1**      |    **458.0 μs** |    **366.80 μs** |    **20.11 μs** |   **5.8594** |        **-** |        **-** |    **29.52 KB** |
| **PostBody** | **Ocelot**             | **10240**  | **17,465.4 μs** | **18,920.35 μs** | **1,037.09 μs** | **406.2500** | **375.0000** |        **-** | **10316.13 KB** |
| **PostBody** | **Ocelot-retry**       | **1**      |    **508.6 μs** |  **1,146.49 μs** |    **62.84 μs** |   **7.8125** |        **-** |        **-** |    **41.49 KB** |
| **PostBody** | **Ocelot-retry**       | **10240**  | **20,273.9 μs** | **15,778.72 μs** |   **864.88 μs** | **687.5000** | **656.2500** | **343.7500** | **20567.17 KB** |

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
| Method     | Gateway      | RouteCount | Mean     | Error      | StdDev    | Gen0    | Allocated |
|----------- |------------- |----------- |---------:|-----------:|----------:|--------:|----------:|
| **ProxiedGet** | **ConduitSharp** | **1**          | **270.5 μs** |   **588.1 μs** |  **32.24 μs** |  **2.9297** |  **14.33 KB** |
| **ProxiedGet** | **ConduitSharp** | **100**        | **231.7 μs** |   **429.6 μs** |  **23.55 μs** |  **2.4414** |  **14.11 KB** |
| **ProxiedGet** | **ConduitSharp** | **500**        | **245.5 μs** |   **499.9 μs** |  **27.40 μs** |  **2.9297** |  **14.48 KB** |
| **ProxiedGet** | **Ocelot**       | **1**          | **389.3 μs** |   **331.8 μs** |  **18.19 μs** |  **4.8828** |  **26.25 KB** |
| **ProxiedGet** | **Ocelot**       | **100**        | **427.3 μs** | **1,591.6 μs** |  **87.24 μs** |  **8.7891** |  **45.97 KB** |
| **ProxiedGet** | **Ocelot**       | **500**        | **851.6 μs** | **5,219.2 μs** | **286.08 μs** | **25.3906** | **127.16 KB** |

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
| **AuthedGet** | **ConduitSharp** | **336.3 μs** | **872.5 μs** | **47.82 μs** | **3.9063** |  **21.29 KB** |
| **AuthedGet** | **Ocelot**       | **547.6 μs** | **917.8 μs** | **50.31 μs** | **6.8359** |  **37.68 KB** |

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
| Method   | RequiredClaims | Mean     | Error     | StdDev   | Gen0   | Allocated |
|--------- |--------------- |---------:|----------:|---------:|-------:|----------:|
| **Validate** | **False**          | **33.13 μs** |  **2.229 μs** | **0.122 μs** | **1.7090** |   **8.95 KB** |
| **Validate** | **True**           | **34.61 μs** | **10.166 μs** | **0.557 μs** | **1.8311** |   **9.51 KB** |

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
| Method  | NoopPlugins | Mean     | Error     | StdDev    | Gen0   | Allocated |
|-------- |------------ |---------:|----------:|----------:|-------:|----------:|
| **Request** | **0**           | **34.76 μs** |  **52.78 μs** |  **2.893 μs** | **1.5869** |   **8.22 KB** |
| **Request** | **1**           | **29.84 μs** |  **20.63 μs** |  **1.131 μs** | **1.5869** |   **8.27 KB** |
| **Request** | **5**           | **30.80 μs** | **224.91 μs** | **12.328 μs** | **1.7090** |   **8.46 KB** |

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
| **MatchLastRoute** | **1**          | **28.99 μs** | **199.36 μs** | **10.928 μs** | **1.5869** |   **8.23 KB** |
| **MatchLastRoute** | **10**         | **26.52 μs** | **125.32 μs** |  **6.869 μs** | **1.5869** |   **8.23 KB** |
| **MatchLastRoute** | **100**        | **37.19 μs** |  **81.49 μs** |  **4.467 μs** | **1.5869** |   **8.23 KB** |
| **MatchLastRoute** | **500**        | **32.23 μs** |  **75.12 μs** |  **4.118 μs** | **1.5869** |   **8.23 KB** |
