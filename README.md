# CSharp-Lookuptables

[![CI](https://github.com/MatthewCarven/CSharp-Lookuptables/actions/workflows/ci.yml/badge.svg)](https://github.com/MatthewCarven/CSharp-Lookuptables/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
[![License: Unlicense](https://img.shields.io/badge/license-Unlicense-blue.svg)](LICENSE)

A C# / .NET 10 port of [Python-Lookuptables](https://github.com/MatthewCarven/Python-Lookuptables), benchmarked
against the built-in .NET collections.

The idea: bucket keys by their first few symbols (letters or bytes) so that a lookup only has to scan one small
list. It also makes a query like "give me everything that starts with these 3 bytes" a direct jump to the
right bucket, instead of a search through everything.

## TL;DR

- **Prefix queries are where this wins.** Finding every key that starts with 3 given bytes among 1M keys takes
  **0.24 µs** with a flat lookup table, against 1.4 µs for a sorted array and **4,145 µs** for `HashSet<T>`, which
  has to scan every key. That makes the table about 17,000x faster than a hash set.
- **For plain "does this key exist?" lookups, `HashSet<T>` is the one to beat.** A flat table ties or beats it
  (9.9 ns vs 21 ns warm, 135 ns vs 129 ns at 1M keys) when each bucket holds about one key, but it costs up to 10x the memory.
- **On disk it scales.** On a 64 GB database, 8x the machine's RAM, the bucket index finds a record in
  **0.36 ms**, while a full scan of the file takes 43 s: about 118,000x faster.
- **The ported Python design (nested dictionaries) is 1.4-6x faster in C# than in Python.** Replacing the
  dictionaries with one flat array gives up to another 3x on top.

## Quick start

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

Run the tests:

```bash
dotnet test
```

Re-run the Python scripts' scenarios in C# (about 25 minutes, mostly spent on the deliberately slow linear list):

```bash
dotnet run -c Release --project bench/Lookuptables.Bench -- python
```

| Suite | What it runs |
|---|---|
| `python` | The in-memory scripts at their original sizes: Bench, Bench2D, Bench3D, BenchBinary/BenchSet/Bench4d |
| `scale` | 1,000,000 keys. Structures averaging over 1,000 keys per bucket are skipped |
| `disk` | BenchDisk2.py. The default database is 125 MB; `--disk-gb 16` matches the Python script, `--disk-gb 64` is the run below |
| `all` | `python` + `scale` + `disk` |
| `bdn --filter '*' --job short` | BenchmarkDotNet microbenchmarks of `Contains` and the prefix query |

`--help` lists the options (`--reps`, `--seed`, `--out`, ...). Results are written to `results/` as Markdown and JSON.

## Using the library

```csharp
using Lookuptables;

// Index binary records by their first 2 bytes (256 * 256 = 65,536 buckets).
var table = new FlatTableBuckets<byte[], ByteKey>(depth: 2);

bool added = table.AddUnique(record);   // false if it was already there
bool found = table.Contains(record);

// Everything that starts with 1F A0 07.
var matches = new List<byte[]>();
table.CollectPrefix([0x1F, 0xA0, 0x07], matches);

// A-Z strings work the same way, with 26 symbols per layer.
var words = new FlatTableBuckets<string, UpperAlphaKey>(depth: 3);
words.AddUnique("HELLO");
words.CollectPrefix("HEL", results);
```

Every structure implements `ILookupSet<T>`, so they can be swapped freely:

| Member | Python equivalent |
|---|---|
| `AddUnique(item)` | `add_unique` |
| `Contains(item)` | `find` |
| `CollectPrefix(prefix, results)` | The "beginning 3 bytes" lookup from the original README |
| `LoadDistinct(items)` | Bulk load without uniqueness checks |

To support another key type, implement `IKeyTraits<T>`, a struct with static members that says how many symbols
there are per layer and how to read, compare and prefix-match keys. See `UpperAlphaKey` and `ByteKey` in
[KeyTraits.cs](src/Lookuptables/KeyTraits.cs).

## What's in the box

```
src/Lookuptables/          the library
bench/Lookuptables.Bench/  benchmark app (Stopwatch harness, scale suite, disk suite, BenchmarkDotNet)
tests/Lookuptables.Tests/  xUnit tests: every structure checked against a HashSet reference
results/                   the numbers quoted below; results/python has the original scripts' output
```

### Python to C# mapping

| Python | C# | Notes |
|---|---|---|
| `StandardList`, `StandardBinaryList` | `LinearList<T, TKey>` | O(N) scan |
| `OneLayerList` ... `FourLayerList`, `BinaryThreeLayerList` | `NestedDictionaryBuckets<T, TKey>(depth)` | Nested `Dictionary<int, ...>` ending in a `List<T>`, the same shape as nested `defaultdict`s. One class covers any depth |
| (new) | `FlatTableBuckets<T, TKey>(depth)` | The same idea written the C# way: the first *depth* symbols become one array index, so there's no hashing at all. It allocates Radix^depth slots up front |
| `set()` | `HashSetStore<T, TKey>` | Also `FrozenSetStore`, `SortedListStore` (sorted `List<T>` + `BinarySearch`) and `SortedSetStore` as baselines |
| `DiskIndexer`, `linear_disk_search_batched` | `DiskIndexer`, `FlatFileScanner` | Same `root/b0/b1/bucket_b2.bin` layout on disk |
| `str` / `bytes` keys | `UpperAlphaKey` / `ByteKey` | Static abstract interface members, so the JIT compiles a specialised copy of each structure per key type, with no virtual calls |

## Results

Measured on an Intel i9-11900H laptop with .NET 10.0.12 on Windows 11. Full tables:
[csharp-python.md](results/csharp-python.md), [csharp-scale.md](results/csharp-scale.md),
[csharp-disk.md](results/csharp-disk.md), [BenchmarkDotNet reports](results/bdn/results).

### 1. Prefix queries: the lookup table's real advantage

"Everything starting with these 3 symbols", 1,000 queries against 1M keys:

| Structure | 1M words | 1M binary records |
|---|--:|--:|
| Flat table (3 symbols / 3 bytes) | **0.33 µs** | **0.24 µs** |
| Nested dict, 3 layers (Python port) | 0.41 µs | 0.71 µs |
| Sorted `List<T>` + `BinarySearch` | 2.8 µs | 1.4 µs |
| `SortedSet<T>` | 6.0 µs | 3.0 µs |
| `HashSet<T>` (scans everything) | 7,242 µs | 4,145 µs |
| `FrozenSet<T>` (scans everything) | 62,295 µs | 67,727 µs |

A hash set can't answer a prefix query without checking every key. The bucketed table jumps straight to the
answer, making it 17,000-22,000x faster than `HashSet`, and 5-10x faster than a sorted array, the best
general-purpose alternative.

### 2. Exact lookups: `HashSet<T>` is hard to beat

Cost per lookup with 1,000,000 keys loaded, 1,000,000 lookups (50% hits), median of 7 runs:

| Structure | 1M 6-letter words | 1M 16-byte records | Memory (1M records) |
|---|--:|--:|--:|
| `HashSet<T>` | **165 ns** | **129 ns** | 22 MB |
| Flat table, 3 bytes | - | 135 ns | 209 MB |
| Flat table, 4 letters | 460 ns | - | - |
| Nested dict, 3 layers (Python port) | 1,971 ns | 440 ns | 162 MB |
| Sorted `List<T>` + `BinarySearch` | 631 ns | 1,639 ns | 8 MB |
| `SortedSet<T>` | 800 ns | 1,852 ns | 46 MB |

With random bytes, a 3-byte flat table gives nearly every key its own bucket. That makes it a perfect hash, and it matches
`HashSet` while using 10x the memory. Words don't spread that evenly: 3 letters give only 17,576 buckets, so at 1M words each
bucket holds about 57 words, and the scan inside the bucket dominates.

**Warm cache (BenchmarkDotNet).** Here each table holds 100k keys, and the same 1,000 lookups are repeated until the timings settle,
so everything stays in CPU cache. Under those conditions the flat table's arithmetic indexing beats hashing:

| `Contains`, 100k keys, per lookup | 6-letter words | 16-byte records |
|---|--:|--:|
| Flat table, 4 letters / 3 bytes | **9.9 ns** | **9.9 ns** |
| Flat table, 3 letters / 2 bytes | 40.4 ns | 13.7 ns |
| `FrozenSet<T>` | 11.4 ns | 18.3 ns |
| `HashSet<T>` | 12.7 ns | 21.0 ns |
| Nested dict, 3 layers (Python port) | 95.9 ns | 39.6 ns |
| Sorted `List<T>` + `BinarySearch` | 253.8 ns | 213.6 ns |
| `SortedSet<T>` | 267.4 ns | 293.2 ns |
| `List<T>` linear scan | 369,047 ns | 316,536 ns |

### 3. C# vs Python, same scenarios

Python numbers come from running the original scripts on the same machine ([results/python](results/python)),
converted to time per operation.

**BenchSet.py / Bench4d.py: 100k 16-byte records, search per lookup**

| Structure | Python 3.14 | C# port | C# flat table |
|---|--:|--:|--:|
| Linear list | 746 µs | 256 µs | |
| 3-layer buckets | 625 ns | 355 ns | 126 ns (2 bytes) |
| 4-layer buckets | 577 ns | 423 ns | |
| `set` / `HashSet<T>` | 182 ns | 117 ns | |

**Bench3D.py: 100k 6-letter words, search per lookup**

| Structure | Python 3.14 | C# port | C# flat table |
|---|--:|--:|--:|
| Linear list | 908 µs | 290 µs | |
| 1 layer | 45.9 µs | 12.2 µs | 13.5 µs |
| 2 layers | 2.34 µs | 580 ns | 558 ns |
| 3 layers | 808 ns | 136 ns | 96 ns |
| `HashSet<T>` (not in the Python script) | | 41 ns | |

The layering behaves the same way in both languages: each extra layer divides the scan by the number of symbols, so the
Python scripts' 1,000x+ speed-ups over a plain list carry over.

### 4. Disk index (BenchDisk2.py), 64 GB

1,048,592 records of 64 KB each, stored twice: a 64 GB flat file and a 64 GB bucket index, 128 GB in total. That is 8x the
machine's 15.8 GB of RAM, so neither the OS file cache nor the SSD's own cache can hold it, and the reads really come off
the SSD. The run made 131,070 lookups, half of them hits spread across every record on disk
([results/csharp-disk.md](results/csharp-disk.md)).

| Method | Per lookup | All 131,070 lookups |
|---|--:|--:|
| Indexed: open one bucket file and read ~64 KB | **0.36 ms** | 47 s |
| Batched full scan of the flat file (5 samples, 913 MB/s) | 42.7 s | ~1,554 hours (projected) |

That's a **~118,000x** speed-up. Writing the database took 24 minutes, averaging 90 MB/s: the SSD slowed down once its
fast write cache filled, and each record also creates or appends to a small index file. For comparison, a 125 MB
database that fits in RAM gives 0.057 ms per indexed lookup, so reading from the SSD itself adds about 0.3 ms per lookup.

To reproduce this, run `disk --disk-gb 64`. It needs about 135 GB free, and it deletes the generated files when it finishes.

## Methodology and differences from the Python scripts

- **Every answer is checked.** Every repetition rebuilds the structure from scratch. After a JIT warm-up, the reported time is the median of 7 runs, and each run checks its counts, hits and prefix matches against expected answers computed independently.
- **Seeded data.** Data comes from `Random(42)` rather than `os.urandom`, so runs are reproducible (`--seed` to change).
- **Half the searches hit.** Searches are 50% existing keys and 50% random keys. The Python scripts searched random keys only, so they almost always missed.
- **Bulk pre-load.** Pre-loading uses a bulk load of already-distinct keys instead of N `add_unique` calls. The final state is the same; only the timed insert phase uses `AddUnique`.
- **Disk lookups spread across every record.** Python keeps ~100 records and looks them up over and over, so after the first pass every hit comes from the OS file cache, and each miss holds a new 64 KB record in RAM. Here each record's contents are generated from its record number, so any record can be looked up without keeping it in memory, and the hits land all over the disk.
- **Record-aligned disk scan.** The disk scan compares whole records. Python's `target in chunk_batch` is a substring search, which could in theory match across two records.
- **Noise in the small scenarios.** The Python-sized scenarios use only 1,000 searches, so they're single-pass and vary by about ±30% between runs. The 1M-key suite and BenchmarkDotNet numbers are the more reliable ones.

## License

Public domain under the [Unlicense](LICENSE), the same as the original.
