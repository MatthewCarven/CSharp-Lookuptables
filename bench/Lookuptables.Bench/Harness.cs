using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lookuptables;

namespace Lookuptables.Bench;

public enum KeyKind { UpperAlpha, Bytes, SequentialIds }

public sealed record Scenario(
    string Id,
    string Title,
    string PythonSource,
    KeyKind Kind,
    int KeyLength,
    int Pool,
    int Inserts,
    int Searches,
    int Prefixes,
    int PrefixLength,
    int? MaxMeanScan);

public sealed record StructureResult(
    string Structure,
    Family Family,
    double BuildMs,
    double? InsertNsPerOp,
    double SearchNsPerOp,
    double PrefixUsPerQuery,
    double MemoryMB,
    string? Shape);

public sealed record ScenarioResult(Scenario Scenario, int PoolDistinct, int FinalDistinct, IReadOnlyList<StructureResult> Results, IReadOnlyList<string> Skipped);

/// <summary>
/// Stopwatch harness that mirrors the Python scripts (pre-load, time N inserts, time N searches)
/// and adds a prefix-query phase. Every repetition rebuilds the structure from scratch, results are
/// the median of the measured repetitions, and every answer is checked against the dataset.
/// </summary>
public sealed class Harness(int reps, TimeSpan minWarmup)
{
    public ScenarioResult Run<T>(Scenario scenario, Dataset<T> data, IReadOnlyList<StructureFactory<T>> structures)
        where T : notnull
    {
        var results = new List<StructureResult>();
        var skipped = new List<string>();
        foreach (StructureFactory<T> factory in structures)
        {
            // A fixed table whose buckets hold thousands of keys is a linear scan in disguise; at 1M keys it
            // would take hours. The estimate uses the real bucket sizes, so skewed data is judged fairly.
            double? meanScan = factory.IndexDepth is { } depth ? data.MeanScanLength(depth) : null;
            if (meanScan > scenario.MaxMeanScan)
            {
                skipped.Add($"{factory.Name} (~{meanScan:N0} keys per lookup)");
                continue;
            }
            Console.Write($"  {factory.Name,-32}");
            StructureResult result = Measure(factory, data);
            results.Add(result);
            Console.WriteLine($" search {result.SearchNsPerOp,12:N1} ns/op{(result.Shape is null ? "" : "  [" + result.Shape + "]")}");
        }
        return new ScenarioResult(scenario, data.Pool.Length, data.FinalDistinct.Length, results, skipped);
    }

    private StructureResult Measure<T>(StructureFactory<T> factory, Dataset<T> data) where T : notnull
    {
        var build = new List<double>();
        var insert = new List<double>();
        var search = new List<double>();
        var prefix = new List<double>();
        double memoryMB = 0;
        bool insertTimed = false;
        string? shape = null;

        // Warm up for a fixed time rather than a fixed count: tiered JIT goes tier-0 -> instrumented -> tier-1,
        // and each step waits for the code to stay hot for ~100 ms. A capped count on a small data set can finish
        // before the optimised code arrives and then measure unoptimised code.
        var warmupClock = Stopwatch.StartNew();
        int warmups = 0;
        while (warmups < 2 || warmupClock.Elapsed < minWarmup)
        {
            RunOnce(factory, data, out _, out _);
            warmups++;
        }

        for (int rep = 0; rep < reps; rep++)
        {
            Timings t = RunOnce(factory, data, out insertTimed, out shape);
            build.Add(t.BuildMs);
            insert.Add(t.InsertMs);
            search.Add(t.SearchMs);
            prefix.Add(t.PrefixMs);
            memoryMB = t.MemoryBytes / (1024.0 * 1024.0);
        }

        return new StructureResult(
            factory.Name,
            factory.Family,
            Median(build),
            insertTimed ? Median(insert) * 1e6 / data.NewItems.Length : null,
            Median(search) * 1e6 / data.SearchTerms.Length,
            Median(prefix) * 1e3 / data.Prefixes.Length,
            memoryMB,
            shape);
    }

    private readonly record struct Timings(double BuildMs, double InsertMs, double SearchMs, double PrefixMs, long MemoryBytes);

    private static Timings RunOnce<T>(StructureFactory<T> factory, Dataset<T> data, out bool insertTimed, out string? shape)
        where T : notnull
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);

        long start = Stopwatch.GetTimestamp();
        ILookupSet<T> store = factory.Create();
        insertTimed = store.SupportsInsert;
        // A build-once structure gets the final contents directly, so the later phases see the same data.
        store.LoadDistinct(insertTimed ? data.Pool : data.FinalDistinct);
        double buildMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // Keys are shared between structures, so this is the structure's own overhead.
        long memoryBytes = GC.GetTotalMemory(forceFullCollection: true) - memoryBefore;

        double insertMs = 0;
        if (insertTimed)
        {
            start = Stopwatch.GetTimestamp();
            int added = InsertAll(store, data.NewItems);
            insertMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Check(factory, "added", data.ExpectedAdded, added);
        }
        Check(factory, "count", data.FinalDistinct.Length, store.Count);

        start = Stopwatch.GetTimestamp();
        int hits = SearchAll(store, data.SearchTerms);
        double searchMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Check(factory, "hits", data.ExpectedHits, hits);

        var buffer = new List<T>(256);
        start = Stopwatch.GetTimestamp();
        long matches = PrefixAll(store, data.Prefixes, buffer);
        double prefixMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Check(factory, "prefix matches", data.ExpectedPrefixMatches, matches);

        shape = store.Shape;
        return new Timings(buildMs, insertMs, searchMs, prefixMs, memoryBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int InsertAll<T>(ILookupSet<T> store, T[] items)
    {
        int added = 0;
        foreach (T item in items)
        {
            if (store.AddUnique(item))
                added++;
        }
        return added;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int SearchAll<T>(ILookupSet<T> store, T[] items)
    {
        int hits = 0;
        foreach (T item in items)
        {
            if (store.Contains(item))
                hits++;
        }
        return hits;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long PrefixAll<T>(ILookupSet<T> store, T[] prefixes, List<T> buffer)
    {
        long total = 0;
        foreach (T prefix in prefixes)
        {
            buffer.Clear();
            total += store.CollectPrefix(prefix, buffer);
        }
        return total;
    }

    private static void Check<T>(StructureFactory<T> factory, string what, long expected, long actual) where T : notnull
    {
        if (expected != actual)
            throw new InvalidOperationException($"{factory.Name}: expected {what} = {expected}, got {actual}.");
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}
