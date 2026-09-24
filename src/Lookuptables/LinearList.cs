namespace Lookuptables;

/// <summary>
/// Port of <c>StandardList</c> / <c>StandardBinaryList</c>: one flat list, every operation is an O(N) scan.
/// </summary>
public sealed class LinearList<T, TKey> : ILookupSet<T>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    private readonly List<T> _items = [];

    public int Count => _items.Count;

    public void LoadDistinct(ReadOnlySpan<T> items) => _items.AddRange(items);

    public bool AddUnique(T item)
    {
        if (Contains(item))
            return false;
        _items.Add(item);
        return true;
    }

    public bool Contains(T item) => Buckets.Contains<T, TKey>(_items, item);

    public int CollectPrefix(T prefix, List<T> results)
    {
        int before = results.Count;
        foreach (T item in _items)
        {
            if (TKey.StartsWith(item, prefix))
                results.Add(item);
        }
        return results.Count - before;
    }
}
