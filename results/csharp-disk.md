# C# results

- Environment: .NET 10.0.12, Microsoft Windows 10.0.22631, 11th Gen Intel(R) Core(TM) i9-11900H @ 2.50GHz (16 logical cores)

## Disk index (BenchDisk2.py)

- Records: 1,048,592 x 65,535 bytes = 64.00 GB flat file (directory index not built in this run)
- Machine RAM: 15.8 GB; the data on disk is 4.1x RAM
- Lookups: 131,070 per method, 50% hits spread over all records; each method gets its own lookups
- Flat file written in 325 s (202 MB/s)
- Burst trie index built in 0.4 s, 77 MB of RAM (257 inner nodes, 65,536 leaves (avg 16.0 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped)
- Dictionary index built in 0.1 s, 58 MB of RAM
- Sequential read speed: 985 MB/s (single pass), 1,012 MB/s (per-lookup samples)

| Method | Index | All 131,070 lookups | Per lookup |
|---|---|--:|--:|
| Linear scan, one full pass per lookup (projected from 5) | none | 8,237,703 s (2,288 h) | 62,850 ms |
| Linear scan, one pass answering every lookup at once | none | 66.6 s | 0.508 ms (average) |
| Burst trie in RAM, then one read from the flat file | 77 MB RAM, 0.4 s to build | 19.8 s | 0.151 ms |
| `Dictionary<UInt128, long>` in RAM, then one read from the flat file | 58 MB RAM, 0.1 s to build | 18.8 s | 0.144 ms |

Answering all 131,070 lookups, compared with the single-pass scan: the burst trie is 3.4x faster, the dictionary is 3.5x faster. Compared with one pass per lookup: the burst trie is 416,983x faster, the dictionary is 437,570x faster.

The data is larger than RAM, so most reads come from the SSD rather than the OS file cache (recently written data can still be cached, so a small share of lookups may be served from RAM).

Note added by hand: the per-lookup scan projection above uses the raw average of its 5 samples, 4 of which happened to be
misses (a miss reads the whole file; a hit stops, on average, halfway). At the lookups' actual 50% hit rate, the
expected cost is about 0.5 x 64.8 s + 0.5 x 32.4 s = 48.6 s per lookup, or about 1,770 hours for all 131,070. The
README uses that figure.
