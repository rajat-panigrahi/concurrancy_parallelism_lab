```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 8.0.130
  [Host] : .NET 8.0.30 (8.0.3026.36720), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  short  : .NET 8.0.30 (8.0.3026.36720), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=short  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                         | Mean        | Error       | StdDev      | Ratio  | RatioSD | Allocated | Alloc Ratio |
|------------------------------- |------------:|------------:|------------:|-------:|--------:|----------:|------------:|
| &#39;No synchronisation (unsafe)&#39;  |    352.8 μs |    175.4 μs |     9.61 μs |   1.00 |    0.03 |       6 B |        1.00 |
| Interlocked.Increment          |  4,815.9 μs | 12,355.5 μs |   677.25 μs |  13.66 |    1.70 |       6 B |        1.00 |
| lock                           | 19,258.2 μs | 18,968.6 μs | 1,039.73 μs |  54.61 |    2.87 |      23 B |        3.83 |
| &#39;ReaderWriterLockSlim (write)&#39; | 20,885.0 μs | 12,978.6 μs |   711.40 μs |  59.23 |    2.25 |      23 B |        3.83 |
| SemaphoreSlim.Wait             | 41,369.4 μs | 10,340.1 μs |   566.78 μs | 117.31 |    3.14 |      61 B |       10.17 |
