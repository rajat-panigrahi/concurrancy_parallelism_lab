```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 8.0.130
  [Host] : .NET 8.0.30 (8.0.3026.36720), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  short  : .NET 8.0.30 (8.0.3026.36720), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=short  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                       | Mean          | Error        | StdDev      | Ratio    | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------------------- |--------------:|-------------:|------------:|---------:|--------:|-------:|----------:|------------:|
| &#39;Synchronous call&#39;                           |      6.000 μs |     5.977 μs |   0.3276 μs |     1.00 |    0.07 |      - |         - |          NA |
| &#39;async Task, always completed&#39;               |    128.311 μs |   320.683 μs |  17.5777 μs |    21.43 |    2.75 | 5.2490 |  720072 B |          NA |
| &#39;async ValueTask, always completed&#39;          |     12.954 μs |     7.097 μs |   0.3890 μs |     2.16 |    0.12 |      - |         - |          NA |
| &#39;Task.Run wrapping sync work (anti-pattern)&#39; | 11,121.993 μs | 4,633.208 μs | 253.9618 μs | 1,857.56 |   97.83 |      - | 1360216 B |          NA |
