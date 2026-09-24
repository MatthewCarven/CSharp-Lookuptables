using System.Runtime.InteropServices;

namespace Lookuptables;

/// <summary>
/// Faithful port of <c>OneLayerList</c>, <c>TwoLayerList</c>, <c>ThreeLayerList</c> and <c>FourLayerList</c>:
/// nested <c>collections.defaultdict</c>s keyed by the first <c>depth</c> symbols, ending in a list.
/// Each layer costs one hash lookup.
/// </summary>
public sealed class NestedDictionaryBuckets<T, TKey> : ILookupSet<T>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    private sealed class Node
    {
        public Dictionary<int, Node>? Children;
        public List<T>? Items;
    }

    private readonly Node _root = new();
    private readonly int _depth;

    public NestedDictionaryBuckets(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        _depth = depth;
    }

    public int Depth => _depth;

    public int Count { get; private set; }

    public void LoadDistinct(ReadOnlySpan<T> items)
    {
        foreach (T item in items)
            GetOrCreateBucket(item).Add(item);
        Count += items.Length;
    }

    public bool AddUnique(T item)
    {
        // defaultdict semantics: self.buckets[c1][c2][c3] creates the path even before the check.
        List<T> bucket = GetOrCreateBucket(item);
        if (Buckets.Contains<T, TKey>(bucket, item))
            return false;
        bucket.Add(item);
        Count++;
        return true;
    }

    public bool Contains(T item)
    {
        // Python's find() checks "c1 in buckets and c2 in buckets[c1] ..." so it never creates buckets.
        Node? node = _root;
        for (int i = 0; i < _depth; i++)
        {
            if (node.Children is null || !node.Children.TryGetValue(TKey.SymbolAt(item, i), out node))
                return false;
        }
        return node.Items is { } bucket && Buckets.Contains<T, TKey>(bucket, item);
    }

    public int CollectPrefix(T prefix, List<T> results)
    {
        int before = results.Count;
        int prefixLength = TKey.Length(prefix);
        int walk = Math.Min(prefixLength, _depth);

        Node? node = _root;
        for (int i = 0; i < walk; i++)
        {
            if (node.Children is null || !node.Children.TryGetValue(TKey.SymbolAt(prefix, i), out node))
                return 0;
        }

        if (prefixLength >= _depth)
        {
            if (node.Items is { } bucket)
                Buckets.CollectMatches<T, TKey>(bucket, prefix, bucketIsExact: prefixLength == _depth, results);
        }
        else
        {
            // Prefix is shorter than the index: every key below this node matches.
            CollectSubtree(node, results);
        }
        return results.Count - before;
    }

    private static void CollectSubtree(Node node, List<T> results)
    {
        if (node.Items is { } bucket)
            results.AddRange(bucket);
        if (node.Children is { } children)
        {
            foreach (Node child in children.Values)
                CollectSubtree(child, results);
        }
    }

    private List<T> GetOrCreateBucket(T item)
    {
        Node node = _root;
        for (int i = 0; i < _depth; i++)
        {
            node.Children ??= [];
            ref Node? child = ref CollectionsMarshal.GetValueRefOrAddDefault(node.Children, TKey.SymbolAt(item, i), out _);
            node = child ??= new Node();
        }
        return node.Items ??= [];
    }
}
