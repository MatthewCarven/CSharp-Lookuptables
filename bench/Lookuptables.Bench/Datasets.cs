using System.Runtime.InteropServices;
using Lookuptables;

namespace Lookuptables.Bench;

/// <summary>One benchmark workload, plus the answers every structure must reproduce.</summary>
public sealed class Dataset<T> where T : notnull
{
    /// <summary>Distinct keys pre-loaded before timing (Python's <c>data_pool</c> after <c>add_unique</c>).</summary>
    public required T[] Pool { get; init; }

    /// <summary>Keys inserted with AddUnique (Python's <c>new_items</c>); may collide with the pool.</summary>
    public required T[] NewItems { get; init; }

    /// <summary>Lookups: half are keys that exist, half are fresh random keys (almost all misses).</summary>
    public required T[] SearchTerms { get; init; }

    /// <summary>Prefix queries ("the beginning 3 bytes"), cut from existing keys so they have matches.</summary>
    public required T[] Prefixes { get; init; }

    /// <summary>Pool plus the new items: the contents after the insert phase.</summary>
    public required T[] FinalDistinct { get; init; }

    public required int ExpectedAdded { get; init; }
    public required int ExpectedHits { get; init; }
    public required long ExpectedPrefixMatches { get; init; }

    /// <summary>
    /// For a fixed table indexed by the first <c>depth</c> symbols: how many keys the average stored key shares
    /// its bucket with (sum of squared bucket sizes / N). Equals N / buckets for evenly spread keys, and
    /// approaches N when the keys all share a prefix.
    /// </summary>
    public required Func<int, double> MeanScanLength { get; init; }
}

public static class DatasetFactory
{
    public static Dataset<string> Alpha(int keyLength, int pool, int inserts, int searches, int prefixes, int prefixLength, int seed)
    {
        var random = new Random(seed);
        return Build<string, UpperAlphaKey>(
            () => string.Create(keyLength, random, static (span, r) =>
            {
                for (int i = 0; i < span.Length; i++)
                    span[i] = (char)('A' + r.Next(26));
            }),
            key => key[..prefixLength],
            pool, inserts, searches, prefixes, prefixLength, random);
    }

    public static Dataset<byte[]> Bytes(int keyLength, int pool, int inserts, int searches, int prefixes, int prefixLength, int seed)
    {
        var random = new Random(seed);
        return Build<byte[], ByteKey>(
            () =>
            {
                var bytes = new byte[keyLength];
                random.NextBytes(bytes);
                return bytes;
            },
            key => key[..prefixLength],
            pool, inserts, searches, prefixes, prefixLength, random);
    }

    /// <summary>
    /// Sparse sequential IDs, stored as 8-byte big-endian numbers drawn from [0, 4 x pool): the top bytes are
    /// always zero, so a table indexed by the first bytes puts every key in the same bucket.
    /// </summary>
    public static Dataset<byte[]> SequentialIds(int pool, int inserts, int searches, int prefixes, int prefixLength, int seed)
    {
        var random = new Random(seed);
        long range = 4L * pool;
        return Build<byte[], ByteKey>(
            () =>
            {
                var bytes = new byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, random.NextInt64(range));
                return bytes;
            },
            key => key[..prefixLength],
            pool, inserts, searches, prefixes, prefixLength, random);
    }

    private static Dataset<T> Build<T, TKey>(
        Func<T> next, Func<T, T> toPrefix, int poolSize, int inserts, int searches, int prefixes, int prefixLength, Random random)
        where T : notnull
        where TKey : IKeyTraits<T>
    {
        var seen = new HashSet<T>(TKey.EqualityComparer);

        // Like the Python scripts: generate N random keys, duplicates are dropped by add_unique.
        var pool = new List<T>(poolSize);
        for (int i = 0; i < poolSize; i++)
        {
            T key = next();
            if (seen.Add(key))
                pool.Add(key);
        }

        T[] newItems = new T[inserts];
        var final = new List<T>(pool);
        int added = 0;
        for (int i = 0; i < inserts; i++)
        {
            newItems[i] = next();
            if (seen.Add(newItems[i]))
            {
                final.Add(newItems[i]);
                added++;
            }
        }

        T[] searchTerms = new T[searches];
        int hits = 0;
        for (int i = 0; i < searches; i++)
        {
            searchTerms[i] = i % 2 == 0 ? final[random.Next(final.Count)] : next();
            if (seen.Contains(searchTerms[i]))
                hits++;
        }
        random.Shuffle(searchTerms);

        T[] prefixArray = new T[prefixes];
        for (int i = 0; i < prefixes; i++)
            prefixArray[i] = toPrefix(final[random.Next(final.Count)]);

        return new Dataset<T>
        {
            Pool = [.. pool],
            NewItems = newItems,
            SearchTerms = searchTerms,
            Prefixes = prefixArray,
            FinalDistinct = [.. final],
            ExpectedAdded = added,
            ExpectedHits = hits,
            ExpectedPrefixMatches = CountPrefixMatches<T, TKey>(final, prefixArray, prefixLength),
            MeanScanLength = depth => MeanScanLength<T, TKey>(pool, depth),
        };
    }

    private static double MeanScanLength<T, TKey>(List<T> keys, int depth)
        where T : notnull
        where TKey : IKeyTraits<T>
    {
        double sumOfSquares = 0;
        foreach (int size in PrefixHistogram<T, TKey>(keys, depth).Values)
            sumOfSquares += (double)size * size;
        return sumOfSquares / keys.Count;
    }

    /// <summary>Independent answer for the prefix queries: histogram of every key's prefix, O(N + P).</summary>
    private static long CountPrefixMatches<T, TKey>(List<T> keys, T[] prefixes, int prefixLength)
        where T : notnull
        where TKey : IKeyTraits<T>
    {
        Dictionary<long, int> histogram = PrefixHistogram<T, TKey>(keys, prefixLength);
        long total = 0;
        foreach (T prefix in prefixes)
            total += histogram.GetValueOrDefault(PrefixCode<T, TKey>(prefix, prefixLength));
        return total;
    }

    /// <summary>Number of keys per distinct prefix of <paramref name="length"/> symbols.</summary>
    private static Dictionary<long, int> PrefixHistogram<T, TKey>(List<T> keys, int length)
        where T : notnull
        where TKey : IKeyTraits<T>
    {
        var histogram = new Dictionary<long, int>();
        foreach (T key in keys)
            CollectionsMarshal.GetValueRefOrAddDefault(histogram, PrefixCode<T, TKey>(key, length), out _)++;
        return histogram;
    }

    private static long PrefixCode<T, TKey>(T key, int length)
        where T : notnull
        where TKey : IKeyTraits<T>
    {
        long code = 0;
        for (int i = 0; i < length; i++)
            code = checked(code * TKey.Radix + TKey.SymbolAt(key, i));
        return code;
    }
}
