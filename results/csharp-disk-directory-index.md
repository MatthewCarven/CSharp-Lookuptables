# C# results

- Environment: .NET 10.0.12, Microsoft Windows 10.0.22631, 11th Gen Intel(R) Core(TM) i9-11900H @ 2.50GHz (16 logical cores)

## Disk index (BenchDisk2.py)

- Records: 1,048,592 x 65,535 bytes = 64.00 GB flat file + 64.00 GB index
- Machine RAM: 15.8 GB; the database is 8.1x RAM
- Lookups: 131,070 (50% hits, spread over all records); linear scan samples: 5; batch: 1024 records
- Generation: 1,463 s (90 MB/s written, flat file + index)
- Linear scan read speed: 913 MB/s

| Method | Total | Per lookup |
|---|--:|--:|
| Indexed (actual, 131,070 lookups, 65,793 hits) | 47.42 s | 0.3618 ms |
| Linear batched scan (projected from 5) | 5,595,941 s (1,554.43 h) | 42,694.3 ms |

Speed-up: **118,003x**

The database is larger than RAM, so most reads come from the SSD rather than the OS file cache (recently written data can still be cached, so a small share of lookups may be served from RAM).
