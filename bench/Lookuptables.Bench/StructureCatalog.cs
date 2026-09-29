using Lookuptables;

namespace Lookuptables.Bench;

public enum Family
{
    /// <summary>Straight port of a class from the Python repo.</summary>
    PythonPort,

    /// <summary>The same idea rewritten the way C# would do it.</summary>
    CSharpLookupTable,

    /// <summary>A built-in .NET collection - the "primitive" baseline.</summary>
    BuiltIn,

    /// <summary>Buckets that split on demand to follow the data (the burst trie).</summary>
    Adaptive,
}

/// <param name="IndexDepth">Symbols a fixed table indexes by (0 for the plain list); null when the structure adapts.</param>
public sealed record StructureFactory<T>(string Name, Family Family, int? IndexDepth, Func<ILookupSet<T>> Create)
{
    public override string ToString() => Name;
}

public static class StructureCatalog
{
    public static IReadOnlyList<StructureFactory<string>> Alpha() =>
    [
        new("List<T> linear scan", Family.PythonPort, IndexDepth: 0, () => new LinearList<string, UpperAlphaKey>()),
        new("Nested dict, 1 layer", Family.PythonPort, 1, () => new NestedDictionaryBuckets<string, UpperAlphaKey>(1)),
        new("Nested dict, 2 layers", Family.PythonPort, 2, () => new NestedDictionaryBuckets<string, UpperAlphaKey>(2)),
        new("Nested dict, 3 layers", Family.PythonPort, 3, () => new NestedDictionaryBuckets<string, UpperAlphaKey>(3)),
        new("Flat table, 1 symbol", Family.CSharpLookupTable, 1, () => new FlatTableBuckets<string, UpperAlphaKey>(1)),
        new("Flat table, 2 symbols", Family.CSharpLookupTable, 2, () => new FlatTableBuckets<string, UpperAlphaKey>(2)),
        new("Flat table, 3 symbols", Family.CSharpLookupTable, 3, () => new FlatTableBuckets<string, UpperAlphaKey>(3)),
        new("Flat table, 4 symbols", Family.CSharpLookupTable, 4, () => new FlatTableBuckets<string, UpperAlphaKey>(4)),
        .. Shared<string, UpperAlphaKey>(),
    ];

    public static IReadOnlyList<StructureFactory<byte[]>> Bytes() =>
    [
        new("List<T> linear scan", Family.PythonPort, IndexDepth: 0, () => new LinearList<byte[], ByteKey>()),
        new("Nested dict, 3 layers", Family.PythonPort, 3, () => new NestedDictionaryBuckets<byte[], ByteKey>(3)),
        new("Nested dict, 4 layers", Family.PythonPort, 4, () => new NestedDictionaryBuckets<byte[], ByteKey>(4)),
        new("Flat table, 1 byte", Family.CSharpLookupTable, 1, () => new FlatTableBuckets<byte[], ByteKey>(1)),
        new("Flat table, 2 bytes", Family.CSharpLookupTable, 2, () => new FlatTableBuckets<byte[], ByteKey>(2)),
        new("Flat table, 3 bytes", Family.CSharpLookupTable, 3, () => new FlatTableBuckets<byte[], ByteKey>(3)),
        .. Shared<byte[], ByteKey>(),
    ];

    /// <summary>The adaptive structure and the built-in collections, for any key type.</summary>
    private static IEnumerable<StructureFactory<T>> Shared<T, TKey>()
        where T : notnull
        where TKey : IKeyTraits<T> =>
    [
        new("Burst trie (adaptive)", Family.Adaptive, null, () => new BurstTrie<T, TKey>()),
        new("HashSet<T>", Family.BuiltIn, null, () => new HashSetStore<T, TKey>()),
        new("FrozenSet<T>", Family.BuiltIn, null, () => new FrozenSetStore<T, TKey>()),
        new("Sorted List<T> + BinarySearch", Family.BuiltIn, null, () => new SortedListStore<T, TKey>()),
        new("SortedSet<T>", Family.BuiltIn, null, () => new SortedSetStore<T, TKey>()),
    ];

    /// <summary>The burst trie at a range of thresholds, with HashSet as the yardstick.</summary>
    public static IReadOnlyList<StructureFactory<T>> BurstThresholds<T, TKey>()
        where T : notnull
        where TKey : IKeyTraits<T> =>
    [
        .. new[] { 8, 16, 32, 64, 128, 256, 512, 1024 }.Select(threshold => new StructureFactory<T>(
            $"Burst trie, threshold {threshold}", Family.Adaptive, null, () => new BurstTrie<T, TKey>(threshold))),
        new("HashSet<T>", Family.BuiltIn, null, () => new HashSetStore<T, TKey>()),
    ];
}
