```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 8.0.130
  [Host] : .NET 8.0.30 (8.0.3026.36720), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  short  : .NET 8.0.30 (8.0.3026.36720), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=short  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                            | AccountCount | Mean        | Error       | StdDev      | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated | Alloc Ratio |
|---------------------------------- |------------- |------------:|------------:|------------:|------:|--------:|---------:|---------:|---------:|----------:|------------:|
| &#39;Sequential (one core)&#39;           | 100000       |  3,447.0 μs |  1,310.9 μs |    71.85 μs |  1.00 |    0.03 |        - |        - |        - |       3 B |        1.00 |
| &#39;Parallel + lock per item&#39;        | 100000       | 38,212.4 μs | 59,021.7 μs | 3,235.18 μs | 11.09 |    0.84 |        - |        - |        - |    3353 B |    1,117.67 |
| &#39;Parallel + Interlocked per item&#39; | 100000       |  9,375.4 μs |  4,314.5 μs |   236.49 μs |  2.72 |    0.08 |        - |        - |        - |    2492 B |      830.67 |
| &#39;Parallel + thread-local sums&#39;    | 100000       |    964.6 μs |    360.2 μs |    19.74 μs |  0.28 |    0.01 |        - |        - |        - |    2494 B |      831.33 |
| PLINQ                             | 100000       |  1,575.7 μs |  1,063.9 μs |    58.32 μs |  0.46 |    0.02 |        - |        - |        - |    3826 B |    1,275.33 |
| &#39;Parallel + ConcurrentQueue&#39;      | 100000       | 35,704.9 μs | 68,404.8 μs | 3,749.50 μs | 10.36 |    0.96 | 307.6923 | 307.6923 | 307.6923 | 2104358 B |  701,452.67 |
