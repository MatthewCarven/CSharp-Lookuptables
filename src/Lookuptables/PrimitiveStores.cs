using System.Collections.Frozen;

namespace Lookuptables;

// Thin ILookupSet adapters over the built-in .NET collections, so the benchmark can drive
// every structure through the same interface. The prefix query uses whatever the
// collection natively offers: an ordered structure can seek, a hash set has to scan.

/// <summary><see cref="HashSet{T}"/> - the equivalent of Python's <c>set()</c>.</summary>
public sealed class HashSetStore<T, TKey> : ILookupSet<T>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    private readonly HashSet<T> _set = new(TKey.EqualityComparer);

    public int Count => _set.Count;

    public void LoadDistinct(ReadOnlySpan<T> items)
    {
        _set.EnsureCapacity(_set.Count + items.Length);
        foreach (T item in items)
            _set.Add(item);
    }

    public bool AddUnique(T item) => _set.Add(item);

    public bool Contains(T item) => _set.Contains(item);

    public int CollectPrefix(T prefix, List<T> results) => PrefixScan.Collect<T, TKey>(_set, prefix, results);
}

/// <summary>
/// <see cref="System.Collections.Frozen.FrozenSet{T}"/> - a build-once, read-optimised hash set.
/// It cannot be inserted into, so it is loaded with the final data and only its build and lookups are timed.
/// </summary>
public sealed class FrozenSetStore<T, TKey> : ILookupSet<T>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    private FrozenSet<T> _set = FrozenSet<T>.Empty;

    public int Count => _set.Count;

    public bool SupportsInsert => false;

    public void LoadDistinct(ReadOnlySpan<T> items)
    {
        if (_set.Count != 0)
            throw new InvalidOperationException("A frozen set can only be loaded once.");
        _set = items.ToArray().ToFrozenSet(TKey.EqualityComparer);
    }

    public bool AddUnique(T item) => throw new NotSupportedException("FrozenSet is immutable.");

    public bool Contains(T item) => _set.Contains(item);

    public int CollectPrefix(T prefix, List<T> results) => PrefixScan.Collect<T, TKey>(_set, prefix, results);
}

/// <summary>
/// A <see cref="List{T}"/> kept in sorted order: O(log N) search via BinarySearch,
/// O(N) insert (a memmove), and a prefix query that seeks straight to the first match.
/// </summary>
public sealed class SortedListStore<T, TKey> : ILookupSet<T>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    private readonly List<T> _items = [];

    public int Count => _items.Count;

    public void LoadDistinct(ReadOnlySpan<T> items)
    {
        _items.AddRange(items);
        _items.Sort(TKey.Comparer);
    }

    public bool AddUnique(T item)
    {
        int index = _items.BinarySearch(item, TKey.Comparer);
        if (index >= 0)
            return false;
        _items.Insert(~index, item);
        return true;
    }

    public bool Contains(T item) => _items.BinarySearch(item, TKey.Comparer) >= 0;

    public int CollectPrefix(T prefix, List<T> results)
    {
        int before = results.Count;
        // A prefix sorts before everything that extends it, so its insertion point is the first match.
        int index = _items.BinarySearch(prefix, TKey.Comparer);
        if (index < 0)
            index = ~index;
        for (; index < _items.Count && TKey.StartsWith(_items[index], prefix); index++)
            results.Add(_items[index]);
        return results.Count - before;
    }
}

/// <summary><see cref="SortedSet{T}"/> - a red-black tree.</summary>
public sealed class SortedSetStore<T, TKey> : ILookupSet<T>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    private SortedSet<T> _set = new(TKey.Comparer);

    public int Count => _set.Count;

    public void LoadDistinct(ReadOnlySpan<T> items)
    {
        if (_set.Count != 0)
            throw new InvalidOperationException("LoadDistinct requires an empty store.");
        // The IEnumerable constructor sorts once and builds a balanced tree, much faster than N Adds.
        _set = new SortedSet<T>(items.ToArray(), TKey.Comparer);
    }

    public bool AddUnique(T item) => _set.Add(item);

    public bool Contains(T item) => _set.Contains(item);

    public int CollectPrefix(T prefix, List<T> results)
    {
        int before = results.Count;
        if (_set.Count == 0 || TKey.Comparer.Compare(prefix, _set.Max!) > 0)
            return 0;
        foreach (T item in _set.GetViewBetween(prefix, _set.Max!))
        {
            if (!TKey.StartsWith(item, prefix))
                break;
            results.Add(item);
        }
        return results.Count - before;
    }
}

internal static class PrefixScan
{
    public static int Collect<T, TKey>(IEnumerable<T> items, T prefix, List<T> results)
        where T : notnull
        where TKey : IKeyTraits<T>
    {
        int before = results.Count;
        foreach (T item in items)
        {
            if (TKey.StartsWith(item, prefix))
                results.Add(item);
        }
        return results.Count - before;
    }
}
