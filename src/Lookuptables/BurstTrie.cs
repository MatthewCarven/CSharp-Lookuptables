using System.Diagnostics.CodeAnalysis;

namespace Lookuptables;

/// <summary>
/// Dynamically sized buckets: a burst trie with path compression and fingerprinted leaves.
/// </summary>
/// <remarks>
/// <para>
/// Keys start in one small list that is scanned linearly. When a list grows past the burst threshold it
/// "bursts" into a node indexed by a following symbol, and each child starts as a small list again. The index
/// therefore only gets deeper where the data is dense: a small set stays a plain linear scan, uniformly
/// spread keys end up with about as many layers as the fixed tables, and skewed keys get as many layers as
/// they need - with no Radix^depth table allocated up front.
/// </para>
/// <para>
/// A burst branches on the first position where the keys actually differ. Positions every key in the bucket
/// shares (zero variance, such as the always-zero high bytes of sequential IDs) are recorded once on the node
/// and checked against a sample key, instead of costing a level each (path compression). If a later key
/// differs inside such a run, the node is split at that position.
/// </para>
/// <para>
/// Each leaf stores a 64-bit fingerprint next to every key: the key's next 4 symbols, 16 bits each
/// (symbol + 1, or 0 past the end of the key). A lookup scans the fingerprints with a vectorised IndexOf and
/// only compares a full key on a fingerprint match; a prefix query filters up to 4 symbols past the leaf's
/// depth without touching the keys.
/// </para>
/// <para>Burst tries: Heinz, Zobel and Williams, "Burst tries: a fast, efficient data structure for string keys", 2002.</para>
/// </remarks>
public sealed class BurstTrie<T, TKey> : ILookupSet<T>
    where T : notnull
    where TKey : IKeyTraits<T>
{
    public const int DefaultBurstThreshold = 128;

    private const int FingerprintSymbols = 4;
    private const int FingerprintBits = 16;
    private const int InitialLeafCapacity = 4;

    private abstract class Node;

    private sealed class Inner(int skipStart, int depth, T sample) : Node
    {
        public readonly Node?[] Children = new Node?[TKey.Radix];

        /// <summary>
        /// Positions [SkipStart, Depth) are shared by every key below this node; they are checked against
        /// <see cref="Sample"/> instead of having a level each. Children are indexed by the symbol at Depth.
        /// </summary>
        public int SkipStart = skipStart;
        public readonly int Depth = depth;
        public readonly T Sample = sample;

        /// <summary>The (at most one) key that ends exactly at <see cref="Depth"/>.</summary>
        public bool HasTerminal;
        public T? Terminal;

        /// <summary>First position in [SkipStart, Depth) where <paramref name="key"/> leaves the shared run, or Depth if none.</summary>
        public int Mismatch(T key, int keyLength)
        {
            for (int p = SkipStart; p < Depth; p++)
            {
                if (p >= keyLength || TKey.SymbolAt(key, p) != TKey.SymbolAt(Sample, p))
                    return p;
            }
            return Depth;
        }
    }

    private sealed class Leaf : Node
    {
        public ulong[] Fingerprints = new ulong[InitialLeafCapacity];
        public T[] Items = new T[InitialLeafCapacity];
        public int Count;

        public void Add(ulong fingerprint, T item)
        {
            if (Count == Items.Length)
            {
                Array.Resize(ref Fingerprints, Count * 2);
                Array.Resize(ref Items, Count * 2);
            }
            Fingerprints[Count] = fingerprint;
            Items[Count] = item;
            Count++;
        }

        public bool Contains(T key, ulong fingerprint)
        {
            ReadOnlySpan<ulong> fingerprints = Fingerprints.AsSpan(0, Count);
            int offset = 0;
            while (true)
            {
                int found = fingerprints[offset..].IndexOf(fingerprint);
                if (found < 0)
                    return false;
                offset += found;
                if (TKey.AreEqual(Items[offset], key))
                    return true;
                offset++; // fingerprint collision: keep looking
            }
        }
    }

    private readonly int _threshold;
    private Node _root = new Leaf();

    public BurstTrie(int burstThreshold = DefaultBurstThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(burstThreshold, 1);
        if (TKey.Radix >= (1 << FingerprintBits))
            throw new NotSupportedException($"Radix {TKey.Radix} does not fit a {FingerprintBits}-bit fingerprint symbol.");
        _threshold = burstThreshold;
    }

    public int BurstThreshold => _threshold;

    public int Count { get; private set; }

    public string Shape
    {
        get
        {
            var stats = new ShapeCounter();
            stats.Visit(_root, 0);
            return $"{stats.Inner:N0} inner nodes, {stats.Leaves:N0} leaves (avg {(double)stats.LeafKeys / Math.Max(1, stats.Leaves):N1} keys), " +
                   $"levels per key avg {(double)stats.LevelSum / Math.Max(1, Count):N1} / max {stats.MaxLevels}, " +
                   $"{stats.SkippedPositions:N0} shared positions skipped";
        }
    }

    public void LoadDistinct(ReadOnlySpan<T> items)
    {
        foreach (T item in items)
            Insert(item, checkExisting: false);
    }

    public bool AddUnique(T item) => Insert(item, checkExisting: true);

    public bool Contains(T item)
    {
        int length = TKey.Length(item);
        Node node = _root;
        int depth = 0;
        while (node is Inner inner)
        {
            if (inner.Mismatch(item, length) != inner.Depth)
                return false;
            if (inner.Depth == length)
                return inner.HasTerminal && TKey.AreEqual(inner.Terminal!, item);
            Node? child = inner.Children[TKey.SymbolAt(item, inner.Depth)];
            if (child is null)
                return false;
            node = child;
            depth = inner.Depth + 1;
        }
        return ((Leaf)node).Contains(item, Fingerprint(item, depth));
    }

    public int CollectPrefix(T prefix, List<T> results)
    {
        int before = results.Count;
        int length = TKey.Length(prefix);
        Node node = _root;
        int depth = 0;
        while (node is Inner inner)
        {
            // The prefix only has to agree with the shared run as far as the prefix goes.
            int mismatch = inner.Mismatch(prefix, length);
            if (mismatch < inner.Depth && mismatch < length)
                return 0;
            if (length <= inner.Depth)
            {
                CollectAll(inner, results);
                return results.Count - before;
            }
            Node? child = inner.Children[TKey.SymbolAt(prefix, inner.Depth)];
            if (child is null)
                return 0;
            node = child;
            depth = inner.Depth + 1;
        }

        var leaf = (Leaf)node;
        int remaining = length - depth;
        if (remaining <= 0)
        {
            results.AddRange(leaf.Items.AsSpan(0, leaf.Count));
        }
        else
        {
            // Compare up to 4 more symbols through the fingerprints; only longer prefixes need the keys themselves.
            int covered = Math.Min(remaining, FingerprintSymbols);
            ulong mask = ulong.MaxValue << (FingerprintBits * (FingerprintSymbols - covered));
            ulong wanted = Fingerprint(prefix, depth) & mask;
            for (int i = 0; i < leaf.Count; i++)
            {
                if ((leaf.Fingerprints[i] & mask) == wanted &&
                    (remaining <= FingerprintSymbols || TKey.StartsWith(leaf.Items[i], prefix)))
                {
                    results.Add(leaf.Items[i]);
                }
            }
        }
        return results.Count - before;
    }

    /// <summary>
    /// The smallest and largest stored keys that start with <paramref name="prefix"/> (in the order of
    /// <see cref="IKeyTraits{T}.Comparer"/>), without collecting the keys between them. Where the keys are numbered
    /// in sorted order (records in a flat file sorted by key, say), the two ends give the whole run, so a prefix
    /// that matches millions of keys costs no more than one that matches a few.
    /// </summary>
    /// <remarks>
    /// Children are visited in symbol order, which is key order for <see cref="ByteKey"/> and
    /// <see cref="UpperAlphaKey"/>, and a key that ends at a node comes before every key below it. Only the leaves
    /// at the two ends are scanned (each holds at most the burst threshold's keys).
    /// </remarks>
    /// <returns>False if no stored key starts with the prefix.</returns>
    public bool TryGetPrefixRange(T prefix, [MaybeNullWhen(false)] out T first, [MaybeNullWhen(false)] out T last)
    {
        first = last = default;
        int length = TKey.Length(prefix);
        Node node = _root;
        int depth = 0;
        while (node is Inner inner)
        {
            int mismatch = inner.Mismatch(prefix, length);
            if (mismatch < inner.Depth && mismatch < length)
                return false;
            if (length <= inner.Depth)
            {
                // Every key below this node starts with the prefix.
                first = Smallest(inner);
                last = Largest(inner);
                return true;
            }
            Node? child = inner.Children[TKey.SymbolAt(prefix, inner.Depth)];
            if (child is null)
                return false;
            node = child;
            depth = inner.Depth + 1;
        }

        // A leaf: its keys share the path so far, so check the rest of the prefix (fingerprints first, as
        // CollectPrefix does) and keep the smallest and largest that match.
        var leaf = (Leaf)node;
        int remaining = length - depth;
        int covered = Math.Clamp(remaining, 0, FingerprintSymbols);
        ulong mask = covered == 0 ? 0 : ulong.MaxValue << (FingerprintBits * (FingerprintSymbols - covered));
        ulong wanted = covered == 0 ? 0 : Fingerprint(prefix, depth) & mask;
        bool found = false;
        for (int i = 0; i < leaf.Count; i++)
        {
            T item = leaf.Items[i];
            if ((leaf.Fingerprints[i] & mask) != wanted || (remaining > FingerprintSymbols && !TKey.StartsWith(item, prefix)))
                continue;
            if (!found || TKey.Comparer.Compare(item, first!) < 0)
                first = item;
            if (!found || TKey.Comparer.Compare(item, last!) > 0)
                last = item;
            found = true;
        }
        return found;
    }

    /// <summary>The smallest key at or below a node: a key ending there, or else down the first child.</summary>
    private static T Smallest(Node node)
    {
        while (node is Inner inner)
        {
            if (inner.HasTerminal)
                return inner.Terminal!;
            node = FirstChild(inner, ascending: true) ?? throw new InvalidOperationException("An inner node with no keys.");
        }
        return Extreme((Leaf)node, sign: -1);
    }

    /// <summary>The largest key at or below a node: down the last child, or the key ending there if it has none.</summary>
    private static T Largest(Node node)
    {
        while (node is Inner inner)
        {
            Node? child = FirstChild(inner, ascending: false);
            if (child is null)
                return inner.HasTerminal ? inner.Terminal! : throw new InvalidOperationException("An inner node with no keys.");
            node = child;
        }
        return Extreme((Leaf)node, sign: 1);
    }

    private static Node? FirstChild(Inner inner, bool ascending)
    {
        Node?[] children = inner.Children;
        for (int i = 0; i < children.Length; i++)
        {
            Node? child = children[ascending ? i : children.Length - 1 - i];
            if (child is not null)
                return child;
        }
        return null;
    }

    /// <summary>A leaf's smallest key (<paramref name="sign"/> -1) or largest (+1).</summary>
    private static T Extreme(Leaf leaf, int sign)
    {
        if (leaf.Count == 0)
            throw new InvalidOperationException("An empty leaf below an inner node.");
        T best = leaf.Items[0];
        for (int i = 1; i < leaf.Count; i++)
        {
            if (Math.Sign(TKey.Comparer.Compare(leaf.Items[i], best)) == sign)
                best = leaf.Items[i];
        }
        return best;
    }

    private bool Insert(T item, bool checkExisting)
    {
        int length = TKey.Length(item);
        Inner? parent = null;
        int parentSymbol = 0;
        Node node = _root;
        int depth = 0;
        while (node is Inner inner)
        {
            int mismatch = inner.Mismatch(item, length);
            if (mismatch != inner.Depth)
            {
                // The key leaves the shared run at 'mismatch': split the run there.
                Replace(parent, parentSymbol, SplitRun(inner, item, length, mismatch));
                Count++;
                return true;
            }

            if (inner.Depth == length)
            {
                // Same path and same length means it is the same key.
                if (inner.HasTerminal)
                    return false;
                inner.HasTerminal = true;
                inner.Terminal = item;
                Count++;
                return true;
            }

            int symbol = TKey.SymbolAt(item, inner.Depth);
            Node? child = inner.Children[symbol];
            if (child is null)
            {
                var fresh = new Leaf();
                fresh.Add(Fingerprint(item, inner.Depth + 1), item);
                inner.Children[symbol] = fresh;
                Count++;
                return true;
            }
            parent = inner;
            parentSymbol = symbol;
            node = child;
            depth = inner.Depth + 1;
        }

        var leaf = (Leaf)node;
        ulong fingerprint = Fingerprint(item, depth);
        if (checkExisting && leaf.Contains(item, fingerprint))
            return false;
        leaf.Add(fingerprint, item);
        Count++;

        if (leaf.Count > _threshold)
            Replace(parent, parentSymbol, Burst(leaf, depth));
        return true;
    }

    private void Replace(Inner? parent, int symbol, Node node)
    {
        if (parent is null)
            _root = node;
        else
            parent.Children[symbol] = node;
    }

    /// <summary>
    /// A new key agrees with <paramref name="inner"/>'s shared run only up to <paramref name="at"/>. Insert a node
    /// that branches at that position: one child is the old node (its run now starts after it), the other the new key.
    /// </summary>
    private static Inner SplitRun(Inner inner, T item, int length, int at)
    {
        var split = new Inner(inner.SkipStart, at, inner.Sample);
        inner.SkipStart = at + 1;
        split.Children[TKey.SymbolAt(inner.Sample, at)] = inner;

        if (length == at)
        {
            split.HasTerminal = true;
            split.Terminal = item;
        }
        else
        {
            var fresh = new Leaf();
            fresh.Add(Fingerprint(item, at + 1), item);
            split.Children[TKey.SymbolAt(item, at)] = fresh;
        }
        return split;
    }

    /// <summary>
    /// Splits an over-full leaf. It branches at the first position where its keys differ; positions they all
    /// share are skipped. Children that are still over-full split again.
    /// </summary>
    private Node Burst(Leaf leaf, int depth)
    {
        T sample = leaf.Items[0];
        int branch = SharedRunEnd(leaf, depth);
        var inner = new Inner(depth, branch, sample);
        for (int i = 0; i < leaf.Count; i++)
        {
            T item = leaf.Items[i];
            if (TKey.Length(item) == branch)
            {
                inner.HasTerminal = true;
                inner.Terminal = item;
                continue;
            }
            int symbol = TKey.SymbolAt(item, branch);
            if (inner.Children[symbol] is not Leaf child)
                inner.Children[symbol] = child = new Leaf();
            child.Add(Fingerprint(item, branch + 1), item);
        }

        for (int s = 0; s < inner.Children.Length; s++)
        {
            if (inner.Children[s] is Leaf child && child.Count > _threshold)
                inner.Children[s] = Burst(child, branch + 1);
        }
        return inner;
    }

    /// <summary>First position at or after <paramref name="depth"/> where the leaf's keys stop sharing a symbol (or one ends).</summary>
    private static int SharedRunEnd(Leaf leaf, int depth)
    {
        T sample = leaf.Items[0];
        for (int p = depth; ; p++)
        {
            if (p >= TKey.Length(sample))
                return p;
            int symbol = TKey.SymbolAt(sample, p);
            for (int i = 1; i < leaf.Count; i++)
            {
                T item = leaf.Items[i];
                if (p >= TKey.Length(item) || TKey.SymbolAt(item, p) != symbol)
                    return p;
            }
        }
    }

    private static void CollectAll(Node node, List<T> results)
    {
        if (node is Leaf leaf)
        {
            results.AddRange(leaf.Items.AsSpan(0, leaf.Count));
            return;
        }
        var inner = (Inner)node;
        if (inner.HasTerminal)
            results.Add(inner.Terminal!);
        foreach (Node? child in inner.Children)
        {
            if (child is not null)
                CollectAll(child, results);
        }
    }

    /// <summary>The key's 4 symbols starting at <paramref name="depth"/>, packed most significant first.</summary>
    private static ulong Fingerprint(T key, int depth)
    {
        int length = TKey.Length(key);
        ulong fingerprint = 0;
        for (int i = 0; i < FingerprintSymbols; i++)
        {
            int position = depth + i;
            ulong symbol = position < length ? (ulong)TKey.SymbolAt(key, position) + 1 : 0;
            fingerprint = (fingerprint << FingerprintBits) | symbol;
        }
        return fingerprint;
    }

    private struct ShapeCounter
    {
        public int Inner, Leaves, MaxLevels;
        public long LeafKeys, LevelSum, SkippedPositions;

        public void Visit(Node node, int levels)
        {
            MaxLevels = Math.Max(MaxLevels, levels);
            if (node is Leaf leaf)
            {
                Leaves++;
                LeafKeys += leaf.Count;
                LevelSum += (long)leaf.Count * levels;
                return;
            }
            var inner = (Inner)node;
            Inner++;
            SkippedPositions += inner.Depth - inner.SkipStart;
            if (inner.HasTerminal)
                LevelSum += levels;
            foreach (Node? child in inner.Children)
            {
                if (child is not null)
                    Visit(child, levels + 1);
            }
        }
    }
}
