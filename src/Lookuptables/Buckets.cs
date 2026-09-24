using System.Runtime.InteropServices;

namespace Lookuptables;

/// <summary>Helpers shared by the bucketed structures: the "scan the tiny final list" step.</summary>
internal static class Buckets
{
    public static bool Contains<T, TKey>(List<T> bucket, T item)
        where T : notnull
        where TKey : IKeyTraits<T>
    {
        foreach (T candidate in CollectionsMarshal.AsSpan(bucket))
        {
            if (TKey.AreEqual(candidate, item))
                return true;
        }
        return false;
    }

    /// <summary>Adds the bucket's keys that start with <paramref name="prefix"/>, or all of them when the bucket is already exact.</summary>
    public static void CollectMatches<T, TKey>(List<T> bucket, T prefix, bool bucketIsExact, List<T> results)
        where T : notnull
        where TKey : IKeyTraits<T>
    {
        if (bucketIsExact)
        {
            results.AddRange(bucket);
            return;
        }
        foreach (T candidate in CollectionsMarshal.AsSpan(bucket))
        {
            if (TKey.StartsWith(candidate, prefix))
                results.Add(candidate);
        }
    }
}
