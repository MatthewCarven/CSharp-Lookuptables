# C# results

- Environment: .NET 10.0.12, Microsoft Windows 10.0.22631, 11th Gen Intel(R) Core(TM) i9-11900H @ 2.50GHz (16 logical cores)
- Median of 7 measured repetitions after warm-up; every repetition rebuilds the structure.
- Build = bulk load of the pre-loaded keys (FrozenSet: pre-loaded + inserted keys, it is immutable).
- Memory = managed heap growth caused by the structure itself (the keys are shared, not counted).


## Disk index (BenchDisk2.py)

- Records: 2,000 x 65,535 bytes = 0.12 GB
- Lookups: 131,070 (50% hits); linear scan samples: 5; batch: 1024 records
- Generation (flat file + index): 3.07 s

| Method | Total | Per lookup |
|---|--:|--:|
| Indexed (actual, 131,070 lookups, 65,398 hits) | 7.45 s | 0.0568 ms |
| Linear batched scan (projected from 5) | 8,897 s (2.47 h) | 67.9 ms |

Speed-up: **1,195x**

Note: at this size both files fit in the OS page cache, so this mostly measures cached reads rather than the physical disk.
