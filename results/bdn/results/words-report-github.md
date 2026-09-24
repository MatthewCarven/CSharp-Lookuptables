```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.22631.3810/23H2/2023Update/SunValley3)
11th Gen Intel Core i9-11900H 2.50GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | Structure                     | Mean           | Error          | StdDev         | Gen0   | Allocated |
|------------ |------------------------------ |---------------:|---------------:|---------------:|-------:|----------:|
| **Contains**    | **Flat table, 1 symbol**          |  **16,781.979 ns** |   **6,759.014 ns** |    **370.4844 ns** |      **-** |         **-** |
| PrefixQuery | Flat table, 1 symbol          |  23,734.870 ns |  21,944.250 ns |  1,202.8384 ns |      - |         - |
| **Contains**    | **Flat table, 2 symbols**         |     **661.123 ns** |     **193.986 ns** |     **10.6330 ns** |      **-** |         **-** |
| PrefixQuery | Flat table, 2 symbols         |   1,147.421 ns |     350.135 ns |     19.1921 ns |      - |         - |
| **Contains**    | **Flat table, 3 symbols**         |      **40.356 ns** |      **21.198 ns** |      **1.1619 ns** |      **-** |         **-** |
| PrefixQuery | Flat table, 3 symbols         |      32.114 ns |      16.004 ns |      0.8772 ns |      - |         - |
| **Contains**    | **Flat table, 4 symbols**         |       **9.924 ns** |       **5.369 ns** |      **0.2943 ns** |      **-** |         **-** |
| PrefixQuery | Flat table, 4 symbols         |     259.430 ns |     144.463 ns |      7.9185 ns |      - |         - |
| **Contains**    | **FrozenSet&lt;T&gt;**                  |      **11.368 ns** |       **3.311 ns** |      **0.1815 ns** |      **-** |         **-** |
| PrefixQuery | FrozenSet&lt;T&gt;                  | 620,390.167 ns | 480,636.974 ns | 26,345.3345 ns |      - |         - |
| **Contains**    | **HashSet&lt;T&gt;**                    |      **12.742 ns** |       **3.939 ns** |      **0.2159 ns** |      **-** |         **-** |
| PrefixQuery | HashSet&lt;T&gt;                    | 474,305.367 ns | 177,027.411 ns |  9,703.4698 ns |      - |         - |
| **Contains**    | **List&lt;T&gt; linear scan**           | **369,047.033 ns** |  **41,128.908 ns** |  **2,254.4143 ns** |      **-** |         **-** |
| PrefixQuery | List&lt;T&gt; linear scan           | 538,130.333 ns | 241,783.683 ns | 13,252.9796 ns |      - |         - |
| **Contains**    | **Nested dict, 1 layer**          |  **17,073.274 ns** |   **9,200.845 ns** |    **504.3294 ns** |      **-** |         **-** |
| PrefixQuery | Nested dict, 1 layer          |  25,773.183 ns |  13,227.501 ns |    725.0440 ns |      - |         - |
| **Contains**    | **Nested dict, 2 layers**         |     **753.274 ns** |     **496.308 ns** |     **27.2043 ns** |      **-** |         **-** |
| PrefixQuery | Nested dict, 2 layers         |   1,176.534 ns |     421.887 ns |     23.1251 ns |      - |         - |
| **Contains**    | **Nested dict, 3 layers**         |      **95.943 ns** |       **7.476 ns** |      **0.4098 ns** |      **-** |         **-** |
| PrefixQuery | Nested dict, 3 layers         |      92.301 ns |      10.260 ns |      0.5624 ns |      - |         - |
| **Contains**    | **Sorted List&lt;T&gt; + BinarySearch** |     **253.760 ns** |     **101.030 ns** |      **5.5378 ns** |      **-** |         **-** |
| PrefixQuery | Sorted List&lt;T&gt; + BinarySearch |     370.934 ns |      34.797 ns |      1.9073 ns |      - |         - |
| **Contains**    | **SortedSet&lt;T&gt;**                  |     **267.363 ns** |     **102.032 ns** |      **5.5927 ns** |      **-** |         **-** |
| PrefixQuery | SortedSet&lt;T&gt;                  |     997.752 ns |     265.892 ns |     14.5744 ns | 0.0303 |     392 B |
