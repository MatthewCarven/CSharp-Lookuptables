# C# results

- Environment: .NET 10.0.12, Microsoft Windows 10.0.22631, 11th Gen Intel(R) Core(TM) i9-11900H @ 2.50GHz (16 logical cores)
- Median of 7 measured repetitions after warm-up; every repetition rebuilds the structure.
- Build = bulk load of the pre-loaded keys (FrozenSet: pre-loaded + inserted keys, it is immutable).
- Memory = managed heap growth caused by the structure itself (the keys are shared, not counted).

## Bench.py - 1 layer, 10k words

Python source: `Bench.py`. 5-letter A-Z words; 10,000 generated -> 9,995 distinct pre-loaded; 10,000 inserts; 10,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.01 | 41,184.0 | 41,531.0 | 69.73 | 0.1 | 1x | 1,481.66x |
| Nested dict, 1 layer | Python port | 0.12 | 2,038.0 | 2,043.3 | 2.96 | 0.1 | 20x | 72.90x |
| Nested dict, 2 layers | Python port | 0.27 | 118.8 | 111.4 | 0.20 | 0.2 | 373x | 3.98x |
| Nested dict, 3 layers | Python port | 0.75 | 81.2 | 61.9 | 0.08 | 1.3 | 671x | 2.21x |
| Flat table, 1 symbol | C# lookup table | 0.09 | 2,028.0 | 2,018.4 | 3.09 | 0.1 | 21x | 72.01x |
| Flat table, 2 symbols | C# lookup table | 0.18 | 109.7 | 106.3 | 0.19 | 0.1 | 391x | 3.79x |
| Flat table, 3 symbols | C# lookup table | 0.28 | 38.7 | 37.5 | 0.05 | 0.8 | 1,106x | 1.34x |
| Flat table, 4 symbols | C# lookup table | 1.76 | 70.4 | 47.5 | 0.13 | 4.3 | 874x | 1.69x |
| HashSet<T> | Built-in | 0.16 | 23.8 | 28.0 | 65.55 | 0.2 | 1,482x | 1.00x |
| FrozenSet<T> | Built-in | 2.62 | n/a | 32.2 | 172.79 | 0.6 | 1,291x | 1.15x |
| Sorted List<T> + BinarySearch | Built-in | 1.17 | 1,380.4 | 136.5 | 0.18 | 0.1 | 304x | 4.87x |
| SortedSet<T> | Built-in | 1.36 | 177.0 | 165.9 | 0.40 | 0.5 | 250x | 5.92x |

## Bench2D.py - 2 layers, 50k words

Python source: `Bench2D.py`. 5-letter A-Z words; 50,000 generated -> 49,906 distinct pre-loaded; 10,000 inserts; 10,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.03 | 189,406.6 | 154,141.7 | 210.15 | 0.4 | 1x | 4,583.46x |
| Nested dict, 1 layer | Python port | 0.48 | 6,789.0 | 5,639.0 | 9.60 | 0.4 | 27x | 167.68x |
| Nested dict, 2 layers | Python port | 1.11 | 341.8 | 295.4 | 0.53 | 0.7 | 522x | 8.78x |
| Nested dict, 3 layers | Python port | 2.97 | 103.8 | 92.6 | 0.10 | 2.8 | 1,664x | 2.75x |
| Flat table, 1 symbol | C# lookup table | 0.38 | 8,050.0 | 6,713.6 | 9.67 | 0.4 | 23x | 199.63x |
| Flat table, 2 symbols | C# lookup table | 0.69 | 361.0 | 310.3 | 0.50 | 0.7 | 497x | 9.23x |
| Flat table, 3 symbols | C# lookup table | 1.22 | 57.6 | 55.5 | 0.06 | 1.6 | 2,777x | 1.65x |
| Flat table, 4 symbols | C# lookup table | 5.03 | 60.0 | 47.3 | 0.19 | 7.4 | 3,257x | 1.41x |
| HashSet<T> | Built-in | 0.78 | 59.5 | 33.6 | 195.00 | 1.0 | 4,583x | 1.00x |
| FrozenSet<T> | Built-in | 8.52 | n/a | 29.8 | 532.17 | 1.9 | 5,171x | 0.89x |
| Sorted List<T> + BinarySearch | Built-in | 7.61 | 4,708.1 | 177.7 | 0.24 | 0.4 | 867x | 5.29x |
| SortedSet<T> | Built-in | 8.57 | 229.2 | 210.0 | 0.51 | 2.3 | 734x | 6.25x |

## Bench3D.py - 3 layers, 100k words

Python source: `Bench3D.py`. 6-letter A-Z words; 100,000 generated -> 99,983 distinct pre-loaded; 1,000 inserts; 1,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.05 | 349,526.7 | 265,493.7 | 358.86 | 0.8 | 1x | 6,860.30x |
| Nested dict, 1 layer | Python port | 0.97 | 14,529.1 | 10,962.3 | 18.17 | 0.8 | 24x | 283.26x |
| Nested dict, 2 layers | Python port | 2.09 | 628.5 | 500.9 | 0.91 | 1.4 | 530x | 12.94x |
| Nested dict, 3 layers | Python port | 6.09 | 152.9 | 119.7 | 0.12 | 3.3 | 2,218x | 3.09x |
| Flat table, 1 symbol | C# lookup table | 0.74 | 16,458.6 | 12,492.2 | 18.12 | 0.8 | 21x | 322.80x |
| Flat table, 2 symbols | C# lookup table | 1.31 | 685.0 | 535.3 | 0.89 | 1.3 | 496x | 13.83x |
| Flat table, 3 symbols | C# lookup table | 2.63 | 91.6 | 72.6 | 0.07 | 2.1 | 3,657x | 1.88x |
| Flat table, 4 symbols | C# lookup table | 4.96 | 51.5 | 46.3 | 0.22 | 11.0 | 5,734x | 1.20x |
| HashSet<T> | Built-in | 1.73 | 35.0 | 38.7 | 330.16 | 2.1 | 6,860x | 1.00x |
| FrozenSet<T> | Built-in | 15.96 | n/a | 63.9 | 908.14 | 3.2 | 4,155x | 1.65x |
| Sorted List<T> + BinarySearch | Built-in | 16.79 | 8,614.5 | 201.2 | 0.29 | 0.8 | 1,320x | 5.20x |
| SortedSet<T> | Built-in | 18.86 | 284.8 | 227.5 | 0.60 | 4.6 | 1,167x | 5.88x |

## BenchBinary.py / BenchSet.py / Bench4d.py - 100k binary records

Python source: `BenchBinary.py, BenchSet.py, Bench4d.py`. 16-byte random records; 100,000 generated -> 100,000 distinct pre-loaded; 1,000 inserts; 1,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| List<T> linear scan | Python port | 0.06 | 312,184.9 | 235,179.6 | 353.66 | 0.8 | 1x | 5,014.49x |
| Nested dict, 3 layers | Python port | 32.12 | 453.3 | 204.0 | 0.35 | 26.5 | 1,153x | 4.35x |
| Nested dict, 4 layers | Python port | 105.35 | 618.7 | 286.8 | 0.51 | 50.1 | 820x | 6.12x |
| Flat table, 1 byte | C# lookup table | 0.99 | 1,565.6 | 1,182.9 | 1.44 | 1.0 | 199x | 25.22x |
| Flat table, 2 bytes | C# lookup table | 2.92 | 72.4 | 62.5 | 0.10 | 4.8 | 3,763x | 1.33x |
| Flat table, 3 bytes | C# lookup table | 19.59 | 169.2 | 98.1 | 0.17 | 136.3 | 2,397x | 2.09x |
| HashSet<T> | Built-in | 2.45 | 45.5 | 46.9 | 350.11 | 2.1 | 5,014x | 1.00x |
| FrozenSet<T> | Built-in | 12.84 | n/a | 132.4 | 857.15 | 3.2 | 1,776x | 2.82x |
| Sorted List<T> + BinarySearch | Built-in | 18.67 | 8,735.7 | 273.3 | 0.28 | 0.8 | 861x | 5.83x |
| SortedSet<T> | Built-in | 20.57 | 348.0 | 281.1 | 0.62 | 4.6 | 837x | 5.99x |

