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
- **Dynamically sized buckets fix the fixed layouts' blind spot.** A burst trie that splits a bucket only when
  keys pile up in it, and skips byte positions that never vary, handles sequential IDs in 168 ns per lookup. On
  the same data, fixed 3-byte buckets collapse into a linear scan. It answers prefix queries in 0.07-0.33 µs on
  every data shape tested and uses about as much memory as a `HashSet`, which is still 1.1-1.7x faster at plain
  lookups. See [section 5](#5-dynamically-sized-buckets-a-burst-trie).
- **On disk it scales.** On a 64 GB database, 8x the machine's RAM, the bucket index finds a record in
  **0.36 ms**, while a full scan of the file takes 43 s: about 118,000x faster.
- **The ported Python design (nested dictionaries) is 2-6.5x faster in C# than in Python.** Replacing the
  dictionaries with one flat array gives up to another 4x on top.

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
| `scale` | 1,000,000 keys. Fixed tables whose average lookup would scan over 1,000 keys are skipped |
| `adaptive` | The burst trie against everything, on random records, words and sequential IDs |
| `burst-tune` | The burst trie's split threshold, swept from 8 to 1024 |
| `disk` | BenchDisk2.py. The default database is 125 MB; `--disk-gb 16` matches the Python script, `--disk-gb 64` is the run below |
| `all` | `python` + `scale` + `adaptive` + `disk` |
| `bdn --filter '*' --job short` | BenchmarkDotNet microbenchmarks of `Contains` and the prefix query |

`--help` lists the options (`--reps`, `--seed`, `--out`, `--only <scenario>`, ...). Results are written to `results/` as Markdown and JSON.

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

// Or let the buckets size themselves to the data (see section 5).
var ids = new BurstTrie<byte[], ByteKey>();
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
| (new) | `BurstTrie<T, TKey>(burstThreshold)` | Dynamically sized buckets: split only where keys pile up, skip positions that never vary. See [section 5](#5-dynamically-sized-buckets-a-burst-trie) |
| `set()` | `HashSetStore<T, TKey>` | Also `FrozenSetStore`, `SortedListStore` (sorted `List<T>` + `BinarySearch`) and `SortedSetStore` as baselines |
| `DiskIndexer`, `linear_disk_search_batched` | `DiskIndexer`, `FlatFileScanner` | Same `root/b0/b1/bucket_b2.bin` layout on disk |
| `str` / `bytes` keys | `UpperAlphaKey` / `ByteKey` | Static abstract interface members, so the JIT compiles a specialised copy of each structure per key type, with no virtual calls |

## Results

Measured on an Intel i9-11900H laptop with .NET 10.0.12 on Windows 11. Full tables:
[csharp-python.md](results/csharp-python.md), [csharp-scale.md](results/csharp-scale.md),
[csharp-disk.md](results/csharp-disk.md), [csharp-adaptive.md](results/csharp-adaptive.md),
[csharp-burst-tune.md](results/csharp-burst-tune.md), [BenchmarkDotNet reports](results/bdn/results).

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
| Linear list | 746 µs | 246 µs | |
| 3-layer buckets | 625 ns | 212 ns | 50 ns (2 bytes) |
| 4-layer buckets | 577 ns | 286 ns | |
| `set` / `HashSet<T>` | 182 ns | 64 ns | |
| Burst trie (not in the Python scripts) | | 80 ns | |

**Bench3D.py: 100k 6-letter words, search per lookup**

| Structure | Python 3.14 | C# port | C# flat table |
|---|--:|--:|--:|
| Linear list | 908 µs | 276 µs | |
| 1 layer | 45.9 µs | 11.0 µs | 12.2 µs |
| 2 layers | 2.34 µs | 499 ns | 536 ns |
| 3 layers | 808 ns | 124 ns | 73 ns |
| `HashSet<T>` (not in the Python script) | | 38 ns | |
| Burst trie (not in the Python scripts) | | 61 ns | |

The layering behaves the same way in both languages: each extra layer divides the scan by the number of symbols, so the
Python scripts' 1,000x+ speed-ups over a plain list carry over. At this size, a flat table with about one key per
bucket (2 bytes for 100k records) beats `HashSet<T>` too.

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

### 5. Dynamically sized buckets: a burst trie

The fixed layouts decide the number of layers up front. With too few, buckets get huge; with too many, you pay for
empty slots (the 3-byte flat table allocates 128 MB even for 1,000 keys). They also assume the first bytes vary.
Sequential IDs break that: their high bytes are always zero, so every key lands in the same bucket and each lookup
becomes a linear scan.

[`BurstTrie<T, TKey>`](src/Lookuptables/BurstTrie.cs) sizes its buckets from the data instead:

- **Buckets split only when they fill up.** Every bucket starts as a small list that is scanned linearly. When it
  passes 128 keys it *bursts* into a node indexed by the next symbol, and each child starts as a small list again.
  The index only gets deeper where keys are dense.
- **Bytes that never vary are skipped.** A burst branches at the first position where its keys actually differ.
  Positions they all share are recorded once and checked against a sample key instead of costing a level each. If
  a later key differs inside such a run, the node is split at exactly that position (path compression).
- **Fingerprints avoid most key comparisons.** Next to each key, a bucket stores its next 4 symbols packed into
  64 bits. A lookup scans those with a vectorised `IndexOf` and compares a full key only on a match. A prefix query
  filters 4 symbols past the bucket without touching the keys at all.

The structure is known as a burst trie (Heinz, Zobel and Williams, 2002).
Full tables: [csharp-adaptive.md](results/csharp-adaptive.md).

**Lookup time** (1M lookups on the 1M-key sets, 10k on the smaller ones, median of 7 runs):

| Data | Best fixed layout (nested dict or flat table) | Burst trie | `HashSet<T>` |
|---|--:|--:|--:|
| 1k random records | 21 ns (flat, 2 bytes) | 32 ns | 28 ns |
| 1M random records | 149 ns (flat, 3 bytes, **209 MB**) | 226 ns (**28 MB**) | 157 ns (22 MB) |
| 1M 6-letter words | 260 ns (flat, 4 letters) | **159 ns** | 108 ns |
| 10k sequential IDs | 12,921 ns (every key in one bucket) | **60 ns** | 36 ns |
| 1M sequential IDs | not run: ~885,000 keys scanned per lookup | **168 ns** | 140 ns |

**Prefix query time** per query (random records and words: first 3 symbols; IDs: all IDs in one block of 256):

| Data | Best fixed layout | Burst trie | Sorted `List<T>` | `HashSet<T>` |
|---|--:|--:|--:|--:|
| 1M random records | 0.25 µs | 0.33 µs | 0.63 µs | 4,390 µs |
| 1M 6-letter words | 0.21 µs | **0.16 µs** | 2.55 µs | 4,124 µs |
| 10k sequential IDs | 26.8 µs | **0.07 µs** | 0.57 µs | 33.8 µs |
| 1M sequential IDs | - | **0.18 µs** | 2.14 µs | 3,357 µs |

**What it built by itself:**

| Data | Shape |
|---|---|
| 1M random records | 2 levels, 65,536 buckets of ~15 keys |
| 1M words | 3 levels, 17,576 buckets of ~57 keys: the 3-letter layout, found without being told |
| 1M sequential IDs | Skipped the 5 always-zero bytes, then 2 levels, 15,625 buckets of ~57 keys |

**Verdict:**

- **Plain lookups:** it doesn't beat `HashSet<T>`, which is 1.1-1.7x faster in every scenario.
- **Against the fixed layouts:** it beats every one except on perfectly uniform random bytes. There the 3-byte flat
  table is 1.5x faster but uses 7.6x the memory. The trie never degenerates: on sequential IDs the fixed layouts
  are 200-350x slower.
- **Prefix queries:** it's the best or close to the best on every data shape, 2-16x faster than a sorted array,
  and uses about as much memory as a `HashSet`.

So if you only ever ask "is this key present?", use `HashSet<T>`. If you need prefix or range queries on data
whose shape you don't control, this is the structure to use.

**Choosing the threshold** ([csharp-burst-tune.md](results/csharp-burst-tune.md)): below about 64 keys, words and
sequential IDs burst into 1-3 key leaves, and lookups get 30-60% slower while using up to 10x the memory. Every threshold from 128
to 1024 builds the same shape at the same speed, so the default is 128.

## Methodology and differences from the Python scripts

- **Every answer is checked.** Every repetition rebuilds the structure from scratch. After a JIT warm-up, the reported time is the median of 7 runs, and each run checks its counts, hits and prefix matches against expected answers computed independently.
- **Warm-up by time, not by count.** Each structure is warmed up for at least 1.5 s before measuring, so .NET's tiered JIT has finished optimising it. An earlier version stopped after 50 repetitions, which on small data sets measured partly unoptimised code and made the lookup tables look up to 2x slower than they are. The Python-sized results below were re-run after the fix; the 1M-key, disk and BenchmarkDotNet runs were long enough not to be affected.
- **Seeded data.** Data comes from `Random(42)` rather than `os.urandom`, so runs are reproducible (`--seed` to change).
- **Half the searches hit.** Searches are 50% existing keys and 50% random keys. The Python scripts searched random keys only, so they almost always missed.
- **Bulk pre-load.** Pre-loading uses a bulk load of already-distinct keys instead of N `add_unique` calls. The final state is the same; only the timed insert phase uses `AddUnique`.
- **Disk lookups spread across every record.** Python keeps ~100 records and looks them up over and over, so after the first pass every hit comes from the OS file cache, and each miss holds a new 64 KB record in RAM. Here each record's contents are generated from its record number, so any record can be looked up without keeping it in memory, and the hits land all over the disk.
- **Record-aligned disk scan.** The disk scan compares whole records. Python's `target in chunk_batch` is a substring search, which could in theory match across two records.
- **Noise in the small scenarios.** The Python-sized scenarios use only 1,000 searches, so they're single-pass and vary by about ±30% between runs. The 1M-key suite and BenchmarkDotNet numbers are the more reliable ones.

## License

Public domain under the [Unlicense](LICENSE), the same as the original.
