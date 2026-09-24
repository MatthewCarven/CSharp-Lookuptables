namespace Lookuptables;

/// <summary>
/// The operations every benchmarked structure supports: the Python scripts' <c>add_unique</c>
/// and <c>find</c>, plus the prefix query the README describes
/// ("tell me the beginning 3 bytes and I feed back all the possibilities").
/// </summary>
public interface ILookupSet<T>
{
    int Count { get; }

    /// <summary>False for build-once structures such as <see cref="FrozenSetStore{T, TKey}"/>.</summary>
    bool SupportsInsert => true;

    /// <summary>
    /// Bulk-loads an empty store. The caller guarantees <paramref name="items"/> are distinct,
    /// so no uniqueness check is performed.
    /// </summary>
    void LoadDistinct(ReadOnlySpan<T> items);

    /// <summary>Adds <paramref name="item"/> if it is not already present (Python <c>add_unique</c>).</summary>
    /// <returns>True if the item was added.</returns>
    bool AddUnique(T item);

    /// <summary>Python <c>find</c>.</summary>
    bool Contains(T item);

    /// <summary>Appends every stored key that starts with <paramref name="prefix"/> to <paramref name="results"/>.</summary>
    /// <returns>The number of keys appended.</returns>
    int CollectPrefix(T prefix, List<T> results);
}
