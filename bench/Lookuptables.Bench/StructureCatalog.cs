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
}

public sealed record StructureFactory<T>(string Name, Family Family, long? BucketCount, Func<ILookupSet<T>> Create)
{
    /// <summary>Average keys scanned per lookup, for bucketed structures (null for the built-ins).</summary>
    public double? KeysPerBucket(int keys) => BucketCount is { } b ? (double)keys / b : null;

    public override string ToString() => Name;
}

public static class StructureCatalog
{
    public static IReadOnlyList<StructureFactory<string>> Alpha() =>
    [
        new("List<T> linear scan", Family.PythonPort, BucketCount: 1, () => new LinearList<string, UpperAlphaKey>()),
        new("Nested dict, 1 layer", Family.PythonPort, 26, () => new NestedDictionaryBuckets<string, UpperAlphaKey>(1)),
        new("Nested dict, 2 layers", Family.PythonPort, 26 * 26, () => new NestedDictionaryBuckets<string, UpperAlphaKey>(2)),
        new("Nested dict, 3 layers", Family.PythonPort, 26 * 26 * 26, () => new NestedDictionaryBuckets<string, UpperAlphaKey>(3)),
        new("Flat table, 1 symbol", Family.CSharpLookupTable, 26, () => new FlatTableBuckets<string, UpperAlphaKey>(1)),
        new("Flat table, 2 symbols", Family.CSharpLookupTable, 26 * 26, () => new FlatTableBuckets<string, UpperAlphaKey>(2)),
        new("Flat table, 3 symbols", Family.CSharpLookupTable, 26 * 26 * 26, () => new FlatTableBuckets<string, UpperAlphaKey>(3)),
        new("Flat table, 4 symbols", Family.CSharpLookupTable, 26 * 26 * 26 * 26, () => new FlatTableBuckets<string, UpperAlphaKey>(4)),
        .. BuiltIns<string, UpperAlphaKey>(),
    ];

    public static IReadOnlyList<StructureFactory<byte[]>> Bytes() =>
    [
        new("List<T> linear scan", Family.PythonPort, BucketCount: 1, () => new LinearList<byte[], ByteKey>()),
        new("Nested dict, 3 layers", Family.PythonPort, 1L << 24, () => new NestedDictionaryBuckets<byte[], ByteKey>(3)),
        new("Nested dict, 4 layers", Family.PythonPort, 1L << 32, () => new NestedDictionaryBuckets<byte[], ByteKey>(4)),
        new("Flat table, 1 byte", Family.CSharpLookupTable, 256, () => new FlatTableBuckets<byte[], ByteKey>(1)),
        new("Flat table, 2 bytes", Family.CSharpLookupTable, 1 << 16, () => new FlatTableBuckets<byte[], ByteKey>(2)),
        new("Flat table, 3 bytes", Family.CSharpLookupTable, 1L << 24, () => new FlatTableBuckets<byte[], ByteKey>(3)),
        .. BuiltIns<byte[], ByteKey>(),
    ];

    private static IEnumerable<StructureFactory<T>> BuiltIns<T, TKey>()
        where T : notnull
        where TKey : IKeyTraits<T> =>
    [
        new("HashSet<T>", Family.BuiltIn, null, () => new HashSetStore<T, TKey>()),
        new("FrozenSet<T>", Family.BuiltIn, null, () => new FrozenSetStore<T, TKey>()),
        new("Sorted List<T> + BinarySearch", Family.BuiltIn, null, () => new SortedListStore<T, TKey>()),
        new("SortedSet<T>", Family.BuiltIn, null, () => new SortedSetStore<T, TKey>()),
    ];
}
