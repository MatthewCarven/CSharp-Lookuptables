# C# results

- Environment: .NET 10.0.12, Microsoft Windows 10.0.22631, 11th Gen Intel(R) Core(TM) i9-11900H @ 2.50GHz (16 logical cores)
- Median of 7 measured repetitions after warm-up; every repetition rebuilds the structure.
- Build = bulk load of the pre-loaded keys (FrozenSet: pre-loaded + inserted keys, it is immutable).
- Memory = managed heap growth caused by the structure itself (the keys are shared, not counted).

## Adaptive - 1k random binary records

Python source: `(new)`. 16-byte random records; 1,000 generated -> 1,000 distinct pre-loaded; 1,000 inserts; 10,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.01 | 4,458.2 | 2,833.5 | 7.10 | 0.0 | 1x | 100.91x |
| Nested dict, 3 layers | Python port | 0.09 | 98.8 | 32.9 | 0.06 | 0.4 | 86x | 1.17x |
| Nested dict, 4 layers | Python port | 0.12 | 131.7 | 42.7 | 0.10 | 0.7 | 66x | 1.52x |
| Flat table, 1 byte | C# lookup table | 0.02 | 44.5 | 35.6 | 0.06 | 0.0 | 80x | 1.27x |
| Flat table, 2 bytes | C# lookup table | 0.18 | 32.0 | 21.0 | 0.02 | 0.6 | 135x | 0.75x |
| Flat table, 3 bytes | C# lookup table | 7.79 | 166.3 | 66.6 | 0.04 | 128.1 | 43x | 2.37x |
| Burst trie (adaptive) | Adaptive (new) | 0.03 | 41.4 | 32.0 | 0.05 | 0.0 | 89x | 1.14x |
| HashSet<T> | Built-in | 0.02 | 26.8 | 28.1 | 6.91 | 0.0 | 101x | 1.00x |
| FrozenSet<T> | Built-in | 0.14 | n/a | 22.9 | 18.77 | 0.1 | 124x | 0.82x |
| Sorted List<T> + BinarySearch | Built-in | 0.09 | 233.1 | 118.5 | 0.12 | 0.0 | 24x | 4.22x |
| SortedSet<T> | Built-in | 0.11 | 149.1 | 117.8 | 0.29 | 0.0 | 24x | 4.20x |

Shape of Burst trie (adaptive): 1 inner nodes, 256 leaves (avg 7.8 keys), levels per key avg 1.0 / max 1, 0 shared positions skipped.

## Adaptive - 1M random binary records

Python source: `(new)`. 16-byte random records; 1,000,000 generated -> 1,000,000 distinct pre-loaded; 10,000 inserts; 1,000,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| Nested dict, 3 layers | Python port | 1,181.17 | 667.8 | 270.4 | 0.42 | 161.8 | - | 1.73x |
| Nested dict, 4 layers | Python port | 2,392.81 | 704.0 | 346.9 | 0.79 | 394.7 | - | 2.22x |
| Flat table, 2 bytes | C# lookup table | 88.99 | 610.5 | 533.6 | 0.55 | 14.8 | - | 3.41x |
| Flat table, 3 bytes | C# lookup table | 554.93 | 187.2 | 149.0 | 0.25 | 209.4 | - | 0.95x |
| Burst trie (adaptive) | Adaptive (new) | 201.25 | 260.3 | 226.2 | 0.33 | 27.5 | - | 1.45x |
| HashSet<T> | Built-in | 53.46 | 133.9 | 156.5 | 4,390.18 | 22.2 | - | 1.00x |
| FrozenSet<T> | Built-in | 109.80 | n/a | 121.3 | 35,998.31 | 33.6 | - | 0.78x |
| Sorted List<T> + BinarySearch | Built-in | 279.37 | 102,531.8 | 672.7 | 0.63 | 7.6 | - | 4.30x |
| SortedSet<T> | Built-in | 432.87 | 988.3 | 818.8 | 1.23 | 45.8 | - | 5.23x |

Skipped because an average lookup would scan more than 1,000 keys (effectively a linear scan at this size): List<T> linear scan (~1,000,000 keys per lookup); Flat table, 1 byte (~3,907 keys per lookup).

Shape of Burst trie (adaptive): 257 inner nodes, 65,536 leaves (avg 15.4 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

## Adaptive - 1M words

Python source: `(new)`. 6-letter A-Z words; 1,000,000 generated -> 998,348 distinct pre-loaded; 10,000 inserts; 1,000,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| Nested dict, 3 layers | Python port | 70.33 | 1,514.6 | 1,179.1 | 0.30 | 12.1 | - | 10.96x |
| Flat table, 3 symbols | C# lookup table | 41.77 | 1,419.5 | 1,101.7 | 0.21 | 10.9 | - | 10.24x |
| Flat table, 4 symbols | C# lookup table | 135.30 | 332.1 | 260.2 | 1.91 | 38.5 | - | 2.42x |
| Burst trie (adaptive) | Adaptive (new) | 106.42 | 170.7 | 159.1 | 0.16 | 21.5 | - | 1.48x |
| HashSet<T> | Built-in | 34.54 | 127.1 | 107.6 | 4,124.19 | 22.2 | - | 1.00x |
| FrozenSet<T> | Built-in | 205.25 | n/a | 116.6 | 36,340.57 | 33.6 | - | 1.08x |
| Sorted List<T> + BinarySearch | Built-in | 287.32 | 100,804.1 | 591.5 | 2.55 | 7.6 | - | 5.50x |
| SortedSet<T> | Built-in | 373.61 | 927.4 | 719.7 | 6.22 | 45.7 | - | 6.69x |

Skipped because an average lookup would scan more than 1,000 keys (effectively a linear scan at this size): List<T> linear scan (~998,348 keys per lookup); Nested dict, 1 layer (~38,399 keys per lookup); Nested dict, 2 layers (~1,478 keys per lookup); Flat table, 1 symbol (~38,399 keys per lookup); Flat table, 2 symbols (~1,478 keys per lookup).

Shape of Burst trie (adaptive): 703 inner nodes, 17,576 leaves (avg 57.4 keys), levels per key avg 3.0 / max 3, 0 shared positions skipped.

## Adaptive - 10k sequential IDs

Python source: `(new)`. 8-byte big-endian sequential IDs drawn from [0, 40,000); 10,000 generated -> 8,873 distinct pre-loaded; 1,000 inserts; 10,000 searches (50% hits); 1,000 prefix queries of 7 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.02 | 25,851.4 | 20,675.8 | 32.01 | 0.1 | 1x | 577.70x |
| Nested dict, 3 layers | Python port | 0.21 | 16,180.6 | 12,948.1 | 26.84 | 0.1 | 2x | 361.78x |
| Nested dict, 4 layers | Python port | 0.27 | 16,176.9 | 12,920.9 | 26.95 | 0.1 | 2x | 361.02x |
| Flat table, 1 byte | C# lookup table | 0.08 | 25,299.6 | 20,850.3 | 27.63 | 0.1 | 1x | 582.57x |
| Flat table, 2 bytes | C# lookup table | 0.12 | 25,660.0 | 20,587.5 | 27.69 | 0.6 | 1x | 575.23x |
| Flat table, 3 bytes | C# lookup table | 7.06 | 25,482.3 | 20,700.0 | 27.47 | 128.1 | 1x | 578.37x |
| Burst trie (adaptive) | Adaptive (new) | 0.41 | 107.3 | 59.6 | 0.07 | 0.2 | 347x | 1.67x |
| HashSet<T> | Built-in | 0.20 | 75.5 | 35.8 | 33.78 | 0.2 | 578x | 1.00x |
| FrozenSet<T> | Built-in | 0.67 | n/a | 37.4 | 88.56 | 0.6 | 553x | 1.04x |
| Sorted List<T> + BinarySearch | Built-in | 1.50 | 693.8 | 177.2 | 0.57 | 0.1 | 117x | 4.95x |
| SortedSet<T> | Built-in | 1.65 | 223.5 | 175.4 | 2.16 | 0.4 | 118x | 4.90x |

Shape of Burst trie (adaptive): 1 inner nodes, 157 leaves (avg 61.4 keys), levels per key avg 1.0 / max 1, 6 shared positions skipped.

## Adaptive - 1M sequential IDs

Python source: `(new)`. 8-byte big-endian sequential IDs drawn from [0, 4,000,000); 1,000,000 generated -> 884,794 distinct pre-loaded; 10,000 inserts; 1,000,000 searches (50% hits); 1,000 prefix queries of 7 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| Burst trie (adaptive) | Adaptive (new) | 85.04 | 234.8 | 167.8 | 0.18 | 18.5 | - | 1.20x |
| HashSet<T> | Built-in | 34.81 | 160.9 | 140.0 | 3,357.30 | 18.5 | - | 1.00x |
| FrozenSet<T> | Built-in | 92.52 | n/a | 124.9 | 27,597.87 | 28.6 | - | 0.89x |
| Sorted List<T> + BinarySearch | Built-in | 299.26 | 67,947.2 | 646.8 | 2.14 | 6.8 | - | 4.62x |
| SortedSet<T> | Built-in | 358.28 | 931.6 | 766.0 | 5.79 | 40.5 | - | 5.47x |

Skipped because an average lookup would scan more than 1,000 keys (effectively a linear scan at this size): List<T> linear scan (~884,794 keys per lookup); Nested dict, 3 layers (~884,794 keys per lookup); Nested dict, 4 layers (~884,794 keys per lookup); Flat table, 1 byte (~884,794 keys per lookup); Flat table, 2 bytes (~884,794 keys per lookup); Flat table, 3 bytes (~884,794 keys per lookup).

Shape of Burst trie (adaptive): 63 inner nodes, 15,625 leaves (avg 57.1 keys), levels per key avg 2.0 / max 2, 5 shared positions skipped.

