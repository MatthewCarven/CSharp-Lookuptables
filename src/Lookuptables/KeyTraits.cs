using System.Diagnostics.CodeAnalysis;

namespace Lookuptables;

/// <summary>
/// Describes how a key is split into prefix symbols (the "layers" of the lookup table)
/// and how two keys are compared. Implemented by zero-size structs so the JIT
/// specialises every structure for the key type with no virtual calls.
/// </summary>
public interface IKeyTraits<T> where T : notnull
{
    /// <summary>Number of distinct symbols per position (26 for A-Z, 256 for bytes).</summary>
    static abstract int Radix { get; }

    static abstract int Length(T key);

    /// <summary>The symbol at <paramref name="position"/>, in the range [0, Radix).</summary>
    static abstract int SymbolAt(T key, int position);

    static abstract bool AreEqual(T a, T b);

    static abstract bool StartsWith(T key, T prefix);

    /// <summary>Comparer used by the built-in collections (HashSet, FrozenSet, ...).</summary>
    static abstract IEqualityComparer<T> EqualityComparer { get; }

    /// <summary>Ordering used by the sorted collections. A prefix must sort before every key that extends it.</summary>
    static abstract IComparer<T> Comparer { get; }
}

/// <summary>Strings made of the letters A-Z (the Bench.py / Bench2D.py / Bench3D.py data).</summary>
public readonly struct UpperAlphaKey : IKeyTraits<string>
{
    public static int Radix => 26;

    public static int Length(string key) => key.Length;

    public static int SymbolAt(string key, int position)
    {
        int symbol = key[position] - 'A';
        if ((uint)symbol >= 26)
            ThrowNotUpperAlpha(key);
        return symbol;
    }

    public static bool AreEqual(string a, string b) => string.Equals(a, b);

    public static bool StartsWith(string key, string prefix) => key.StartsWith(prefix, StringComparison.Ordinal);

    public static IEqualityComparer<string> EqualityComparer => StringComparer.Ordinal;

    public static IComparer<string> Comparer => StringComparer.Ordinal;

    [DoesNotReturn]
    private static void ThrowNotUpperAlpha(string key) =>
        throw new ArgumentException($"Key '{key}' contains a character outside A-Z.", nameof(key));
}

/// <summary>Raw binary records (the BenchBinary.py / BenchSet.py / Bench4d.py data).</summary>
public readonly struct ByteKey : IKeyTraits<byte[]>
{
    public static int Radix => 256;

    public static int Length(byte[] key) => key.Length;

    public static int SymbolAt(byte[] key, int position) => key[position];

    public static bool AreEqual(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

    public static bool StartsWith(byte[] key, byte[] prefix) => key.AsSpan().StartsWith(prefix);

    public static IEqualityComparer<byte[]> EqualityComparer => ByteArrayComparer.Instance;

    public static IComparer<byte[]> Comparer => ByteArrayComparer.Instance;
}

/// <summary>
/// Value semantics for byte[] (Python's <c>bytes</c> compare by value; C# arrays compare by reference).
/// </summary>
public sealed class ByteArrayComparer : IEqualityComparer<byte[]>, IComparer<byte[]>
{
    public static readonly ByteArrayComparer Instance = new();

    private ByteArrayComparer() { }

    public bool Equals(byte[]? x, byte[]? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && x.AsSpan().SequenceEqual(y));

    public int GetHashCode(byte[] obj)
    {
        var hash = new HashCode();
        hash.AddBytes(obj);
        return hash.ToHashCode();
    }

    public int Compare(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return x.AsSpan().SequenceCompareTo(y);
    }
}
