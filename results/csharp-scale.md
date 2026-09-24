# C# results

- Environment: .NET 10.0.12, Microsoft Windows 10.0.22631, 11th Gen Intel(R) Core(TM) i9-11900H @ 2.50GHz (16 logical cores)
- Median of 7 measured repetitions after warm-up; every repetition rebuilds the structure.
- Build = bulk load of the pre-loaded keys (FrozenSet: pre-loaded + inserted keys, it is immutable).
- Memory = managed heap growth caused by the structure itself (the keys are shared, not counted).

## Scale - 1M words

Python source: `(beyond the Python scripts)`. 6-letter A-Z words; 1,000,000 generated -> 998,348 distinct pre-loaded; 10,000 inserts; 1,000,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| Nested dict, 3 layers | Python port | 101.43 | 2,438.0 | 1,970.5 | 0.41 | 12.2 | - | 11.92x |
| Flat table, 3 symbols | C# lookup table | 59.91 | 2,789.8 | 1,990.9 | 0.33 | 11.0 | - | 12.04x |
| Flat table, 4 symbols | C# lookup table | 267.85 | 556.7 | 460.0 | 4.12 | 38.5 | - | 2.78x |
| HashSet<T> | Built-in | 62.64 | 301.1 | 165.3 | 7,241.58 | 22.2 | - | 1.00x |
| FrozenSet<T> | Built-in | 415.06 | n/a | 165.9 | 62,294.65 | 54.1 | - | 1.00x |
| Sorted List<T> + BinarySearch | Built-in | 273.94 | 104,325.9 | 630.8 | 2.84 | 7.6 | - | 3.82x |
| SortedSet<T> | Built-in | 365.91 | 988.4 | 799.6 | 5.99 | 45.7 | - | 4.84x |

Skipped (more than 1,000 keys per bucket on average, i.e. effectively a linear scan at this size): List<T> linear scan, Nested dict, 1 layer, Nested dict, 2 layers, Flat table, 1 symbol, Flat table, 2 symbols.

## Scale - 1M binary records

Python source: `(beyond the Python scripts)`. 16-byte random records; 1,000,000 generated -> 1,000,000 distinct pre-loaded; 10,000 inserts; 1,000,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| Nested dict, 3 layers | Python port | 1,233.03 | 784.1 | 439.9 | 0.71 | 161.8 | - | 3.40x |
| Nested dict, 4 layers | Python port | 2,204.96 | 856.2 | 477.2 | 0.83 | 394.7 | - | 3.69x |
| Flat table, 2 bytes | C# lookup table | 63.77 | 574.8 | 479.9 | 0.56 | 14.8 | - | 3.71x |
| Flat table, 3 bytes | C# lookup table | 323.18 | 184.0 | 135.2 | 0.24 | 209.4 | - | 1.05x |
| HashSet<T> | Built-in | 43.92 | 130.2 | 129.3 | 4,145.34 | 22.2 | - | 1.00x |
| FrozenSet<T> | Built-in | 352.53 | n/a | 267.7 | 67,726.74 | 54.1 | - | 2.07x |
| Sorted List<T> + BinarySearch | Built-in | 696.27 | 293,478.0 | 1,638.9 | 1.42 | 7.6 | - | 12.68x |
| SortedSet<T> | Built-in | 835.06 | 2,209.1 | 1,851.5 | 2.96 | 45.8 | - | 14.32x |

Skipped (more than 1,000 keys per bucket on average, i.e. effectively a linear scan at this size): List<T> linear scan, Flat table, 1 byte.

