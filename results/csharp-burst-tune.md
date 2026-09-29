# C# results

- Environment: .NET 10.0.12, Microsoft Windows 10.0.22631, 11th Gen Intel(R) Core(TM) i9-11900H @ 2.50GHz (16 logical cores)
- Median of 7 measured repetitions after warm-up; every repetition rebuilds the structure.
- Build = bulk load of the pre-loaded keys (FrozenSet: pre-loaded + inserted keys, it is immutable).
- Memory = managed heap growth caused by the structure itself (the keys are shared, not counted).

## Burst threshold - 1M random binary records

Python source: `(new)`. 16-byte random records; 1,000,000 generated -> 1,000,000 distinct pre-loaded; 10,000 inserts; 1,000,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| Burst trie, threshold 8 | Adaptive (new) | 1,628.49 | 433.8 | 249.9 | 0.38 | 268.0 | - | 1.67x |
| Burst trie, threshold 16 | Adaptive (new) | 461.93 | 773.0 | 256.4 | 0.40 | 126.2 | - | 1.71x |
| Burst trie, threshold 32 | Adaptive (new) | 242.51 | 219.4 | 201.5 | 0.33 | 27.6 | - | 1.35x |
| Burst trie, threshold 64 | Adaptive (new) | 261.63 | 247.8 | 212.1 | 0.39 | 27.5 | - | 1.42x |
| Burst trie, threshold 128 | Adaptive (new) | 256.93 | 255.3 | 257.8 | 0.36 | 27.5 | - | 1.72x |
| Burst trie, threshold 256 | Adaptive (new) | 247.26 | 209.8 | 200.1 | 0.34 | 27.5 | - | 1.34x |
| Burst trie, threshold 512 | Adaptive (new) | 254.94 | 219.9 | 209.9 | 0.32 | 27.5 | - | 1.40x |
| Burst trie, threshold 1024 | Adaptive (new) | 259.02 | 255.6 | 221.3 | 0.30 | 27.5 | - | 1.48x |
| HashSet<T> | Built-in | 57.32 | 134.6 | 149.8 | 4,419.35 | 22.2 | - | 1.00x |

Shape of Burst trie, threshold 8: 63,794 inner nodes, 967,568 leaves (avg 1.0 keys), levels per key avg 3.0 / max 3, 0 shared positions skipped.

Shape of Burst trie, threshold 16: 24,930 inner nodes, 502,624 leaves (avg 2.0 keys), levels per key avg 2.5 / max 3, 0 shared positions skipped.

Shape of Burst trie, threshold 32: 262 inner nodes, 65,695 leaves (avg 15.4 keys), levels per key avg 2.0 / max 3, 0 shared positions skipped.

Shape of Burst trie, threshold 64: 257 inner nodes, 65,536 leaves (avg 15.4 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

Shape of Burst trie, threshold 128: 257 inner nodes, 65,536 leaves (avg 15.4 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

Shape of Burst trie, threshold 256: 257 inner nodes, 65,536 leaves (avg 15.4 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

Shape of Burst trie, threshold 512: 257 inner nodes, 65,536 leaves (avg 15.4 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

Shape of Burst trie, threshold 1024: 257 inner nodes, 65,536 leaves (avg 15.4 keys), levels per key avg 2.0 / max 2, 0 shared positions skipped.

## Burst threshold - 1M words

Python source: `(new)`. 6-letter A-Z words; 1,000,000 generated -> 998,348 distinct pre-loaded; 10,000 inserts; 1,000,000 searches (50% hits); 1,000 prefix queries of 3 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| Burst trie, threshold 8 | Adaptive (new) | 311.77 | 350.9 | 253.1 | 1.61 | 66.0 | - | 1.12x |
| Burst trie, threshold 16 | Adaptive (new) | 340.68 | 349.2 | 250.5 | 1.56 | 65.8 | - | 1.11x |
| Burst trie, threshold 32 | Adaptive (new) | 465.92 | 357.1 | 271.3 | 1.32 | 65.8 | - | 1.20x |
| Burst trie, threshold 64 | Adaptive (new) | 134.33 | 491.8 | 197.7 | 0.37 | 26.8 | - | 0.87x |
| Burst trie, threshold 128 | Adaptive (new) | 104.79 | 188.4 | 184.8 | 0.18 | 21.5 | - | 0.82x |
| Burst trie, threshold 256 | Adaptive (new) | 124.38 | 196.0 | 206.8 | 0.18 | 21.5 | - | 0.91x |
| Burst trie, threshold 512 | Adaptive (new) | 187.85 | 214.3 | 207.9 | 0.19 | 21.5 | - | 0.92x |
| Burst trie, threshold 1024 | Adaptive (new) | 222.07 | 173.2 | 185.8 | 0.17 | 21.5 | - | 0.82x |
| HashSet<T> | Built-in | 66.04 | 233.1 | 226.2 | 7,672.78 | 22.2 | - | 1.00x |

Shape of Burst trie, threshold 8: 18,491 inner nodes, 408,273 leaves (avg 2.5 keys), levels per key avg 4.0 / max 5, 0 shared positions skipped.

Shape of Burst trie, threshold 16: 18,279 inner nodes, 406,781 leaves (avg 2.5 keys), levels per key avg 4.0 / max 4, 0 shared positions skipped.

Shape of Burst trie, threshold 32: 18,275 inner nodes, 406,712 leaves (avg 2.5 keys), levels per key avg 4.0 / max 4, 0 shared positions skipped.

Shape of Burst trie, threshold 64: 3,754 inner nodes, 88,459 leaves (avg 11.4 keys), levels per key avg 3.2 / max 4, 0 shared positions skipped.

Shape of Burst trie, threshold 128: 703 inner nodes, 17,576 leaves (avg 57.4 keys), levels per key avg 3.0 / max 3, 0 shared positions skipped.

Shape of Burst trie, threshold 256: 703 inner nodes, 17,576 leaves (avg 57.4 keys), levels per key avg 3.0 / max 3, 0 shared positions skipped.

Shape of Burst trie, threshold 512: 703 inner nodes, 17,576 leaves (avg 57.4 keys), levels per key avg 3.0 / max 3, 0 shared positions skipped.

Shape of Burst trie, threshold 1024: 703 inner nodes, 17,576 leaves (avg 57.4 keys), levels per key avg 3.0 / max 3, 0 shared positions skipped.

## Burst threshold - 1M sequential IDs

Python source: `(new)`. 8-byte big-endian sequential IDs drawn from [0, 4,000,000); 1,000,000 generated -> 884,794 distinct pre-loaded; 10,000 inserts; 1,000,000 searches (50% hits); 1,000 prefix queries of 7 symbols.

| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |
|---|---|--:|--:|--:|--:|--:|--:|--:|
| Burst trie, threshold 8 | Adaptive (new) | 1,056.45 | 376.1 | 304.1 | 4.23 | 160.1 | - | 1.74x |
| Burst trie, threshold 16 | Adaptive (new) | 984.40 | 412.4 | 303.7 | 4.31 | 160.1 | - | 1.74x |
| Burst trie, threshold 32 | Adaptive (new) | 524.01 | 401.0 | 291.2 | 3.53 | 160.1 | - | 1.67x |
| Burst trie, threshold 64 | Adaptive (new) | 123.00 | 533.1 | 200.4 | 0.65 | 36.5 | - | 1.15x |
| Burst trie, threshold 128 | Adaptive (new) | 91.49 | 247.9 | 182.4 | 0.18 | 18.5 | - | 1.04x |
| Burst trie, threshold 256 | Adaptive (new) | 92.12 | 235.4 | 182.2 | 0.17 | 18.5 | - | 1.04x |
| Burst trie, threshold 512 | Adaptive (new) | 94.26 | 249.3 | 187.5 | 0.21 | 18.5 | - | 1.07x |
| Burst trie, threshold 1024 | Adaptive (new) | 99.24 | 235.4 | 193.5 | 0.18 | 18.5 | - | 1.11x |
| HashSet<T> | Built-in | 48.06 | 172.3 | 174.7 | 3,602.83 | 18.5 | - | 1.00x |

Shape of Burst trie, threshold 8: 15,688 inner nodes, 892,498 leaves (avg 1.0 keys), levels per key avg 3.0 / max 3, 5 shared positions skipped.

Shape of Burst trie, threshold 16: 15,688 inner nodes, 892,498 leaves (avg 1.0 keys), levels per key avg 3.0 / max 3, 5 shared positions skipped.

Shape of Burst trie, threshold 32: 15,688 inner nodes, 892,498 leaves (avg 1.0 keys), levels per key avg 3.0 / max 3, 5 shared positions skipped.

Shape of Burst trie, threshold 64: 2,178 inner nodes, 157,221 leaves (avg 5.7 keys), levels per key avg 2.2 / max 3, 5 shared positions skipped.

Shape of Burst trie, threshold 128: 63 inner nodes, 15,625 leaves (avg 57.1 keys), levels per key avg 2.0 / max 2, 5 shared positions skipped.

Shape of Burst trie, threshold 256: 63 inner nodes, 15,625 leaves (avg 57.1 keys), levels per key avg 2.0 / max 2, 5 shared positions skipped.

Shape of Burst trie, threshold 512: 62 inner nodes, 15,617 leaves (avg 57.1 keys), levels per key avg 2.0 / max 2, 5 shared positions skipped.

Shape of Burst trie, threshold 1024: 62 inner nodes, 15,617 leaves (avg 57.1 keys), levels per key avg 2.0 / max 2, 5 shared positions skipped.

