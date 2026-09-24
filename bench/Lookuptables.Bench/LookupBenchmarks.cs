using BenchmarkDotNet.Attributes;
using Lookuptables;

namespace Lookuptables.Bench;

/// <summary>
/// BenchmarkDotNet cross-check of the read paths (Contains and the prefix query) on the
/// Bench3D.py / BenchSet.py sized data: 100,000 pre-loaded keys, 1,000 operations per invocation.
/// </summary>
[MemoryDiagnoser]
[GenericTypeArguments(typeof(string), typeof(UpperAlphaKey))]
[GenericTypeArguments(typeof(byte[]), typeof(ByteKey))]
public class LookupBenchmarks<T, TKey>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    private const int Operations = 1000;

    private ILookupSet<T> _store = null!;
    private T[] _searchTerms = null!;
    private T[] _prefixes = null!;
    private readonly List<T> _buffer = new(256);

    [ParamsSource(nameof(StructureNames))]
    public string Structure { get; set; } = "";

    public IEnumerable<string> StructureNames => Catalog().Select(s => s.Name);

    [GlobalSetup]
    public void Setup()
    {
        Dataset<T> data = CreateData();
        _store = Catalog().Single(s => s.Name == Structure).Create();
        _store.LoadDistinct(data.FinalDistinct);
        _searchTerms = data.SearchTerms;
        _prefixes = data.Prefixes;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    public int Contains()
    {
        int hits = 0;
        foreach (T item in _searchTerms)
        {
            if (_store.Contains(item))
                hits++;
        }
        return hits;
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    public long PrefixQuery()
    {
        long total = 0;
        foreach (T prefix in _prefixes)
        {
            _buffer.Clear();
            total += _store.CollectPrefix(prefix, _buffer);
        }
        return total;
    }

    private static IReadOnlyList<StructureFactory<T>> Catalog() =>
        (IReadOnlyList<StructureFactory<T>>)(typeof(T) == typeof(string) ? (object)StructureCatalog.Alpha() : StructureCatalog.Bytes());

    private static Dataset<T> CreateData() =>
        (Dataset<T>)(typeof(T) == typeof(string)
            ? (object)DatasetFactory.Alpha(6, 100_000, 0, Operations, Operations, 3, seed: 42)
            : DatasetFactory.Bytes(16, 100_000, 0, Operations, Operations, 3, seed: 42));
}
