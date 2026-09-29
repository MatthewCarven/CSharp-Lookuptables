using Lookuptables;

namespace Lookuptables.Tests;

public class LookupSetTests
{
    public static TheoryData<string> AlphaStructures => new(AlphaFactories.Keys);
    public static TheoryData<string> ByteStructures => new(ByteFactories.Keys);

    private static readonly Dictionary<string, Func<ILookupSet<string>>> AlphaFactories = new()
    {
        ["Linear"] = () => new LinearList<string, UpperAlphaKey>(),
        ["Nested1"] = () => new NestedDictionaryBuckets<string, UpperAlphaKey>(1),
        ["Nested3"] = () => new NestedDictionaryBuckets<string, UpperAlphaKey>(3),
        ["Nested4"] = () => new NestedDictionaryBuckets<string, UpperAlphaKey>(4),
        ["Flat1"] = () => new FlatTableBuckets<string, UpperAlphaKey>(1),
        ["Flat3"] = () => new FlatTableBuckets<string, UpperAlphaKey>(3),
        ["Flat4"] = () => new FlatTableBuckets<string, UpperAlphaKey>(4),
        ["HashSet"] = () => new HashSetStore<string, UpperAlphaKey>(),
        ["SortedList"] = () => new SortedListStore<string, UpperAlphaKey>(),
        ["SortedSet"] = () => new SortedSetStore<string, UpperAlphaKey>(),
        ["Burst1"] = () => new BurstTrie<string, UpperAlphaKey>(1),
        ["Burst2"] = () => new BurstTrie<string, UpperAlphaKey>(2),
        ["BurstDefault"] = () => new BurstTrie<string, UpperAlphaKey>(),
    };

    private static readonly Dictionary<string, Func<ILookupSet<byte[]>>> ByteFactories = new()
    {
        ["Linear"] = () => new LinearList<byte[], ByteKey>(),
        ["Nested3"] = () => new NestedDictionaryBuckets<byte[], ByteKey>(3),
        ["Nested4"] = () => new NestedDictionaryBuckets<byte[], ByteKey>(4),
        ["Flat1"] = () => new FlatTableBuckets<byte[], ByteKey>(1),
        ["Flat2"] = () => new FlatTableBuckets<byte[], ByteKey>(2),
        ["HashSet"] = () => new HashSetStore<byte[], ByteKey>(),
        ["SortedList"] = () => new SortedListStore<byte[], ByteKey>(),
        ["SortedSet"] = () => new SortedSetStore<byte[], ByteKey>(),
        ["Burst1"] = () => new BurstTrie<byte[], ByteKey>(1),
        ["Burst2"] = () => new BurstTrie<byte[], ByteKey>(2),
        ["BurstDefault"] = () => new BurstTrie<byte[], ByteKey>(),
    };

    [Theory]
    [MemberData(nameof(AlphaStructures))]
    public void AlphaStructure_MatchesReference(string name)
    {
        // A tiny alphabet (A-D) forces plenty of duplicates and shared prefixes.
        var random = new Random(1);
        string Next(int length) => string.Create(length, random, (span, r) =>
        {
            for (int i = 0; i < span.Length; i++) span[i] = (char)('A' + r.Next(4));
        });

        AssertMatchesReference(AlphaFactories[name], () => Next(5), length => Next(length), StringComparer.Ordinal,
            (key, prefix) => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ByteStructures))]
    public void ByteStructure_MatchesReference(string name)
    {
        var random = new Random(2);
        byte[] Next(int length)
        {
            var bytes = new byte[length];
            for (int i = 0; i < length; i++) bytes[i] = (byte)random.Next(3); // 0-2 only, for collisions
            return bytes;
        }

        AssertMatchesReference(ByteFactories[name], () => Next(6), Next, ByteArrayComparer.Instance,
            (key, prefix) => key.AsSpan().StartsWith(prefix));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(32)]
    public void BurstTrie_HandlesVariableLengthKeysAndKeysThatArePrefixesOfOthers(int threshold)
    {
        // Lengths 1-6 over A-C: plenty of keys end exactly where others continue ("AB", "ABC", "ABCA", ...).
        var random = new Random(4);
        string Next(int maxLength) => string.Create(random.Next(1, maxLength + 1), random, (span, r) =>
        {
            for (int i = 0; i < span.Length; i++) span[i] = (char)('A' + r.Next(3));
        });

        AssertMatchesReference(() => new BurstTrie<string, UpperAlphaKey>(threshold), () => Next(6), length => Next(length),
            StringComparer.Ordinal, (key, prefix) => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void BurstTrie_StaysALinearListUntilTheThresholdThenBursts()
    {
        var trie = new BurstTrie<byte[], ByteKey>(burstThreshold: 4);
        for (byte i = 0; i < 4; i++)
            trie.AddUnique([i, 0, 0]);
        Assert.StartsWith("0 inner nodes, 1 leaves", trie.Shape);

        trie.AddUnique([9, 0, 0]);
        Assert.StartsWith("1 inner nodes, 5 leaves", trie.Shape);
    }

    [Fact]
    public void BurstTrie_SkipsSharedPositionsAndSplitsThemWhenAKeyDiffers()
    {
        static byte[] Id(long value)
        {
            var bytes = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            return bytes;
        }

        var trie = new BurstTrie<byte[], ByteKey>(burstThreshold: 8);
        var reference = new HashSet<byte[]>(ByteArrayComparer.Instance);
        for (long id = 0; id < 1000; id++)
        {
            trie.AddUnique(Id(id * 7));
            reference.Add(Id(id * 7));
        }
        // Bytes 0-5 are zero for every key, so the root branches straight at byte 6.
        Assert.Contains("levels per key avg 2.0", trie.Shape);

        // A key that differs inside the skipped run (byte 2) and a key that ends inside it both force a split.
        byte[][] breakers = [Id(0x0000_0100_0000_0000), [0, 0, 0]];
        foreach (byte[] key in breakers)
        {
            Assert.True(trie.AddUnique(key));
            Assert.False(trie.AddUnique(key));
            reference.Add(key);
        }

        Assert.Equal(reference.Count, trie.Count);
        foreach (byte[] key in reference)
            Assert.True(trie.Contains(key));
        Assert.False(trie.Contains(Id(1)));
        Assert.False(trie.Contains([0, 0]));

        var results = new List<byte[]>();
        foreach (byte[] prefix in new byte[][] { [], [0], [0, 0, 0], [0, 0, 1], [0, 0, 0, 0, 0, 0, 0x1B] })
        {
            results.Clear();
            int expected = reference.Count(k => k.AsSpan().StartsWith(prefix));
            Assert.Equal(expected, trie.CollectPrefix(prefix, results));
            Assert.True(results.ToHashSet(ByteArrayComparer.Instance).SetEquals(reference.Where(k => k.AsSpan().StartsWith(prefix))));
        }
    }

    [Fact]
    public void FrozenSet_MatchesReferenceAfterBulkLoad()
    {
        string[] items = ["ABC", "ABD", "XYZ", "ABCD"];
        var store = new FrozenSetStore<string, UpperAlphaKey>();
        store.LoadDistinct(items);

        Assert.False(((ILookupSet<string>)store).SupportsInsert);
        Assert.Throws<NotSupportedException>(() => store.AddUnique("QQQ"));
        Assert.True(store.Contains("ABD"));
        Assert.False(store.Contains("ABE"));
        var results = new List<string>();
        Assert.Equal(3, store.CollectPrefix("AB", results));
    }

    [Fact]
    public void ByteKeys_CompareByValueNotReference()
    {
        var store = new NestedDictionaryBuckets<byte[], ByteKey>(3);
        Assert.True(store.AddUnique([1, 2, 3, 4]));
        Assert.False(store.AddUnique([1, 2, 3, 4]));
        Assert.True(store.Contains([1, 2, 3, 4]));
    }

    [Fact]
    public void UpperAlphaKey_RejectsOtherCharacters()
    {
        var store = new FlatTableBuckets<string, UpperAlphaKey>(2);
        Assert.Throws<ArgumentException>(() => store.AddUnique("aBC"));
    }

    [Fact]
    public void FlatTable_RefusesAbsurdMemory()
    {
        // 256^4 slots would be 32 GB of references.
        Assert.Throws<ArgumentOutOfRangeException>(() => new FlatTableBuckets<byte[], ByteKey>(4));
    }

    private static void AssertMatchesReference<T>(
        Func<ILookupSet<T>> factory,
        Func<T> nextKey,
        Func<int, T> nextPrefix,
        IEqualityComparer<T> comparer,
        Func<T, T, bool> startsWith)
        where T : notnull
    {
        var reference = new HashSet<T>(comparer);
        T[] initial = Enumerable.Range(0, 300).Select(_ => nextKey()).Distinct(comparer).ToArray();
        ILookupSet<T> store = factory();
        store.LoadDistinct(initial);
        reference.UnionWith(initial);

        for (int i = 0; i < 1000; i++)
        {
            T key = nextKey();
            Assert.Equal(reference.Add(key), store.AddUnique(key));
            Assert.Equal(reference.Count, store.Count);
        }

        for (int i = 0; i < 500; i++)
        {
            T key = nextKey();
            Assert.Equal(reference.Contains(key), store.Contains(key));
        }

        // Prefix lengths below, at and above every structure's depth.
        var results = new List<T>();
        for (int length = 1; length <= 6; length++)
        {
            for (int i = 0; i < 20; i++)
            {
                T prefix = nextPrefix(length);
                results.Clear();
                int count = store.CollectPrefix(prefix, results);
                var expected = reference.Where(k => startsWith(k, prefix)).ToHashSet(comparer);
                Assert.Equal(expected.Count, count);
                Assert.True(expected.SetEquals(results));
            }
        }
    }
}
