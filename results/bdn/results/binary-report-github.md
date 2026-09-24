```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.22631.3810/23H2/2023Update/SunValley3)
11th Gen Intel Core i9-11900H 2.50GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method      | Structure                     | Mean           | Error            | StdDev          | Gen0   | Allocated |
|------------ |------------------------------ |---------------:|-----------------:|----------------:|-------:|----------:|
| **Contains**    | **Flat table, 1 byte**            |   **1,490.004 ns** |       **374.946 ns** |      **20.5521 ns** |      **-** |         **-** |
| PrefixQuery | Flat table, 1 byte            |   1,770.668 ns |       514.257 ns |      28.1882 ns |      - |         - |
| **Contains**    | **Flat table, 2 bytes**           |      **13.672 ns** |         **2.039 ns** |       **0.1118 ns** |      **-** |         **-** |
| PrefixQuery | Flat table, 2 bytes           |      30.785 ns |        42.789 ns |       2.3454 ns |      - |         - |
| **Contains**    | **Flat table, 3 bytes**           |       **9.857 ns** |         **4.372 ns** |       **0.2396 ns** |      **-** |         **-** |
| PrefixQuery | Flat table, 3 bytes           |      31.106 ns |        25.543 ns |       1.4001 ns |      - |         - |
| **Contains**    | **FrozenSet&lt;T&gt;**                  |      **18.268 ns** |        **18.684 ns** |       **1.0241 ns** |      **-** |         **-** |
| PrefixQuery | FrozenSet&lt;T&gt;                  | 617,519.267 ns |   524,805.505 ns |  28,766.3607 ns |      - |         - |
| **Contains**    | **HashSet&lt;T&gt;**                    |      **21.046 ns** |         **5.958 ns** |       **0.3266 ns** |      **-** |         **-** |
| PrefixQuery | HashSet&lt;T&gt;                    | 474,603.533 ns |    17,440.355 ns |     955.9647 ns |      - |         - |
| **Contains**    | **List&lt;T&gt; linear scan**           | **316,536.017 ns** |   **137,364.981 ns** |   **7,529.4382 ns** |      **-** |         **-** |
| PrefixQuery | List&lt;T&gt; linear scan           | 592,943.000 ns | 1,830,109.114 ns | 100,314.4564 ns |      - |         - |
| **Contains**    | **Nested dict, 3 layers**         |      **39.550 ns** |        **82.247 ns** |       **4.5082 ns** |      **-** |         **-** |
| PrefixQuery | Nested dict, 3 layers         |      89.287 ns |       296.636 ns |      16.2596 ns |      - |         - |
| **Contains**    | **Nested dict, 4 layers**         |      **53.446 ns** |        **57.769 ns** |       **3.1665 ns** |      **-** |         **-** |
| PrefixQuery | Nested dict, 4 layers         |     167.415 ns |        44.116 ns |       2.4181 ns |      - |         - |
| **Contains**    | **Sorted List&lt;T&gt; + BinarySearch** |     **213.618 ns** |        **32.771 ns** |       **1.7963 ns** |      **-** |         **-** |
| PrefixQuery | Sorted List&lt;T&gt; + BinarySearch |     195.323 ns |       111.732 ns |       6.1244 ns |      - |         - |
| **Contains**    | **SortedSet&lt;T&gt;**                  |     **293.214 ns** |       **759.974 ns** |      **41.6567 ns** |      **-** |         **-** |
| PrefixQuery | SortedSet&lt;T&gt;                  |     549.134 ns |       195.207 ns |      10.7000 ns | 0.0303 |     392 B |
