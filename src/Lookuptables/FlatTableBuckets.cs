namespace Lookuptables;

/// <summary>
/// The idiomatic C# form of the layered lookup table: instead of one dictionary per layer,
/// the first <c>depth</c> symbols are combined into a single array index
/// (<c>((s0 * Radix) + s1) * Radix + s2 ...</c>). Every layer is then pure arithmetic and the
/// whole walk is one array access - memory greedy (Radix^depth slots are allocated up front),
/// but no hashing at all.
/// </summary>
public sealed class FlatTableBuckets<T, TKey> : ILookupSet<T>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    /// <summary>2^26 slots = 512 MB of references on 64-bit; anything bigger is refused.</summary>
    public const long MaxSlots = 1L << 26;

    private readonly List<T>?[] _table;
    private readonly int _depth;

    public FlatTableBuckets(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        long slots = SlotCount(depth);
        if (slots > MaxSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(depth),
                $"Radix {TKey.Radix} ^ depth {depth} = {slots:N0} slots, above the {MaxSlots:N0} limit.");
        }
        _depth = depth;
        _table = new List<T>?[slots];
    }

    public static long SlotCount(int depth)
    {
        long slots = 1;
        for (int i = 0; i < depth; i++)
        {
            slots *= TKey.Radix;
            if (slots > MaxSlots) return slots;
        }
        return slots;
    }

    public int Depth => _depth;

    public int Count { get; private set; }

    public void LoadDistinct(ReadOnlySpan<T> items)
    {
        foreach (T item in items)
            (_table[IndexOf(item, _depth)] ??= []).Add(item);
        Count += items.Length;
    }

    public bool AddUnique(T item)
    {
        ref List<T>? slot = ref _table[IndexOf(item, _depth)];
        if (slot is null)
        {
            slot = [item];
        }
        else
        {
            if (Buckets.Contains<T, TKey>(slot, item))
                return false;
            slot.Add(item);
        }
        Count++;
        return true;
    }

    public bool Contains(T item) =>
        _table[IndexOf(item, _depth)] is { } bucket && Buckets.Contains<T, TKey>(bucket, item);

    public int CollectPrefix(T prefix, List<T> results)
    {
        int before = results.Count;
        int prefixLength = TKey.Length(prefix);

        if (prefixLength >= _depth)
        {
            if (_table[IndexOf(prefix, _depth)] is { } bucket)
                Buckets.CollectMatches<T, TKey>(bucket, prefix, bucketIsExact: prefixLength == _depth, results);
        }
        else
        {
            // A shorter prefix owns a contiguous run of slots.
            int span = (int)SlotCount(_depth - prefixLength);
            int start = IndexOf(prefix, prefixLength) * span;
            foreach (List<T>? bucket in _table.AsSpan(start, span))
            {
                if (bucket is not null)
                    results.AddRange(bucket);
            }
        }
        return results.Count - before;
    }

    private static int IndexOf(T key, int symbols)
    {
        int index = 0;
        for (int i = 0; i < symbols; i++)
            index = index * TKey.Radix + TKey.SymbolAt(key, i);
        return index;
    }
}
