# C# results

- Environment: .NET 10.0.12, Microsoft Windows 10.0.22631, 11th Gen Intel(R) Core(TM) i9-11900H @ 2.50GHz (16 logical cores)
- Median of 7 measured repetitions after warm-up; every repetition rebuilds the structure.
- Build = bulk load of the pre-loaded keys (FrozenSet: pre-loaded + inserted keys, it is immutable).
- Memory = managed heap growth caused by the structure itself (the keys are shared, not counted).

## Bench.py - 1 layer, 10k words

Python source: `Bench.py`. 5-letter A-Z words; 10,000 generated -> 9,995 distinct pre-loaded; 10,000 inserts; 10,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.01 | 41,642.7 | 42,674.8 | 70.13 | 0.1 | 1x | 1,525.73x |
| Nested dict, 1 layer | Python port | 0.14 | 1,770.7 | 1,686.7 | 2.94 | 0.1 | 25x | 60.31x |
| Nested dict, 2 layers | Python port | 0.27 | 119.2 | 113.2 | 0.20 | 0.2 | 377x | 4.05x |
| Nested dict, 3 layers | Python port | 0.75 | 82.3 | 61.8 | 0.08 | 1.3 | 691x | 2.21x |
| Flat table, 1 symbol | C# lookup table | 0.10 | 2,113.8 | 2,085.0 | 3.09 | 0.1 | 20x | 74.54x |
| Flat table, 2 symbols | C# lookup table | 0.18 | 110.4 | 107.8 | 0.19 | 0.1 | 396x | 3.85x |
| Flat table, 3 symbols | C# lookup table | 0.29 | 39.0 | 36.6 | 0.05 | 0.8 | 1,166x | 1.31x |
| Flat table, 4 symbols | C# lookup table | 1.73 | 69.8 | 53.0 | 0.14 | 4.3 | 806x | 1.89x |
| Burst trie (adaptive) | Adaptive (new) | 0.36 | 39.7 | 38.2 | 0.09 | 0.3 | 1,118x | 1.37x |
| HashSet<T> | Built-in | 0.17 | 24.6 | 28.0 | 67.33 | 0.2 | 1,526x | 1.00x |
| FrozenSet<T> | Built-in | 2.46 | n/a | 33.8 | 197.36 | 0.6 | 1,263x | 1.21x |
| Sorted List<T> + BinarySearch | Built-in | 1.15 | 1,378.8 | 137.8 | 0.19 | 0.1 | 310x | 4.93x |
| SortedSet<T> | Built-in | 1.32 | 176.1 | 168.7 | 0.40 | 0.5 | 253x | 6.03x |

Shape of Burst trie (adaptive): 27 inner nodes, 676 leaves (avg 29.6 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

## Bench2D.py - 2 layers, 50k words

Python source: `Bench2D.py`. 5-letter A-Z words; 50,000 generated -> 49,906 distinct pre-loaded; 10,000 inserts; 10,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.03 | 198,716.5 | 160,095.2 | 213.08 | 0.4 | 1x | 4,876.49x |
| Nested dict, 1 layer | Python port | 0.51 | 6,906.4 | 5,647.1 | 9.22 | 0.4 | 28x | 172.01x |
| Nested dict, 2 layers | Python port | 1.12 | 342.7 | 296.9 | 0.54 | 0.7 | 539x | 9.04x |
| Nested dict, 3 layers | Python port | 2.99 | 104.4 | 93.7 | 0.10 | 2.8 | 1,710x | 2.85x |
| Flat table, 1 symbol | C# lookup table | 0.43 | 8,270.1 | 6,762.0 | 9.78 | 0.4 | 24x | 205.97x |
| Flat table, 2 symbols | C# lookup table | 0.69 | 370.9 | 308.6 | 0.51 | 0.7 | 519x | 9.40x |
| Flat table, 3 symbols | C# lookup table | 1.25 | 58.6 | 55.7 | 0.06 | 1.6 | 2,876x | 1.70x |
| Flat table, 4 symbols | C# lookup table | 5.24 | 59.3 | 47.6 | 0.18 | 7.4 | 3,365x | 1.45x |
| Burst trie (adaptive) | Adaptive (new) | 1.44 | 46.7 | 45.3 | 0.17 | 1.3 | 3,536x | 1.38x |
| HashSet<T> | Built-in | 0.91 | 81.3 | 32.8 | 201.29 | 1.0 | 4,876x | 1.00x |
| FrozenSet<T> | Built-in | 9.83 | n/a | 48.7 | 616.91 | 1.9 | 3,290x | 1.48x |
| Sorted List<T> + BinarySearch | Built-in | 7.51 | 4,756.3 | 193.8 | 0.26 | 0.4 | 826x | 5.90x |
| SortedSet<T> | Built-in | 8.58 | 235.3 | 214.8 | 0.56 | 2.3 | 745x | 6.54x |

Shape of Burst trie (adaptive): 27 inner nodes, 676 leaves (avg 88.6 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

## Bench3D.py - 3 layers, 100k words

Python source: `Bench3D.py`. 6-letter A-Z words; 100,000 generated -> 99,983 distinct pre-loaded; 1,000 inserts; 1,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.06 | 364,162.8 | 276,490.5 | 359.89 | 0.8 | 1x | 7,200.27x |
| Nested dict, 1 layer | Python port | 0.95 | 14,437.3 | 11,042.8 | 17.69 | 0.8 | 25x | 287.57x |
| Nested dict, 2 layers | Python port | 2.13 | 646.2 | 499.0 | 0.91 | 1.4 | 554x | 12.99x |
| Nested dict, 3 layers | Python port | 6.12 | 156.9 | 123.6 | 0.12 | 3.3 | 2,237x | 3.22x |
| Flat table, 1 symbol | C# lookup table | 0.76 | 16,107.7 | 12,225.4 | 17.81 | 0.8 | 23x | 318.37x |
| Flat table, 2 symbols | C# lookup table | 1.29 | 692.5 | 536.4 | 0.88 | 1.3 | 515x | 13.97x |
| Flat table, 3 symbols | C# lookup table | 2.70 | 93.7 | 73.4 | 0.07 | 2.1 | 3,767x | 1.91x |
| Flat table, 4 symbols | C# lookup table | 5.15 | 61.5 | 49.0 | 0.24 | 11.0 | 5,643x | 1.28x |
| Burst trie (adaptive) | Adaptive (new) | 6.70 | 111.7 | 61.1 | 0.07 | 3.6 | 4,525x | 1.59x |
| HashSet<T> | Built-in | 1.73 | 36.0 | 38.4 | 339.00 | 2.1 | 7,200x | 1.00x |
| FrozenSet<T> | Built-in | 17.26 | n/a | 72.3 | 1,035.03 | 3.2 | 3,824x | 1.88x |
| Sorted List<T> + BinarySearch | Built-in | 16.86 | 8,641.2 | 208.1 | 0.30 | 0.8 | 1,329x | 5.42x |
| SortedSet<T> | Built-in | 18.87 | 288.3 | 239.4 | 0.63 | 4.6 | 1,155x | 6.23x |

Shape of Burst trie (adaptive): 671 inner nodes, 16,718 leaves (avg 6.0 keys), levels per key avg 3.0 / max 3, 0 shared positions skipped.

## BenchBinary.py / BenchSet.py / Bench4d.py - 100k binary records

Python source: `BenchBinary.py, BenchSet.py, Bench4d.py`. 16-byte random records; 100,000 generated -> 100,000 distinct pre-loaded; 1,000 inserts; 1,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.07 | 306,145.3 | 245,817.7 | 356.50 | 0.8 | 1x | 3,840.90x |
| Nested dict, 3 layers | Python port | 32.88 | 463.2 | 211.6 | 0.36 | 26.5 | 1,162x | 3.31x |
| Nested dict, 4 layers | Python port | 98.09 | 487.0 | 286.2 | 0.52 | 50.1 | 859x | 4.47x |
| Flat table, 1 byte | C# lookup table | 0.90 | 1,548.8 | 1,181.3 | 1.45 | 1.0 | 208x | 18.46x |
| Flat table, 2 bytes | C# lookup table | 2.96 | 60.4 | 50.2 | 0.08 | 4.8 | 4,897x | 0.78x |
| Flat table, 3 bytes | C# lookup table | 20.56 | 168.6 | 101.1 | 0.19 | 136.3 | 2,431x | 1.58x |
| Burst trie (adaptive) | Adaptive (new) | 6.70 | 100.7 | 80.3 | 0.09 | 8.0 | 3,061x | 1.25x |
| HashSet<T> | Built-in | 2.47 | 63.2 | 64.0 | 361.18 | 2.1 | 3,841x | 1.00x |
| FrozenSet<T> | Built-in | 12.78 | n/a | 76.0 | 988.14 | 3.2 | 3,234x | 1.19x |
| Sorted List<T> + BinarySearch | Built-in | 18.89 | 8,705.0 | 270.3 | 0.26 | 0.8 | 909x | 4.22x |
| SortedSet<T> | Built-in | 20.63 | 349.7 | 286.1 | 0.61 | 4.6 | 859x | 4.47x |

Shape of Burst trie (adaptive): 257 inner nodes, 51,523 leaves (avg 2.0 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

