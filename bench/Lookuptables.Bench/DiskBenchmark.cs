using System.Diagnostics;
using System.Text;
using Lookuptables;

namespace Lookuptables.Bench;

public sealed record DiskOptions(string Directory, int Records, int RecordSize, int Lookups, int LinearSamples, int BatchRecords, bool Keep, int Seed);

/// <summary>
/// Port of BenchDisk2.py: stream random records into both a flat file and the 3-byte directory index,
/// then compare indexed lookups with batched full-file scans (projected, as in the Python script).
/// </summary>
public static class DiskBenchmark
{
    private const string MarkerFile = ".lookuptables-disk-bench";

    public static string Run(DiskOptions o)
    {
        PrepareDirectory(o.Directory);
        var random = new Random(o.Seed);
        var indexer = new DiskIndexer(Path.Combine(o.Directory, "my_database_index"), o.RecordSize);
        string flatFile = Path.Combine(o.Directory, "huge_flat_file.bin");
        var sb = new StringBuilder();

        void Log(string line)
        {
            Console.WriteLine(line);
            sb.AppendLine(line);
        }

        Log("## Disk index (BenchDisk2.py)");
        Log("");
        Log($"- Records: {o.Records:N0} x {o.RecordSize:N0} bytes = {(double)o.Records * o.RecordSize / (1 << 30):N2} GB");
        Log($"- Lookups: {o.Lookups:N0} (50% hits); linear scan samples: {o.LinearSamples}; batch: {o.BatchRecords} records");

        // Phase 1: generation. Keep ~100 records to search for later, like known_targets in Python.
        var targetIndices = new HashSet<int>(Enumerable.Range(0, 100).Select(_ => random.Next(o.Records)));
        var knownTargets = new List<byte[]>();
        byte[] record = new byte[o.RecordSize];
        var clock = Stopwatch.StartNew();
        using (var flat = new FileStream(flatFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            for (int i = 0; i < o.Records; i++)
            {
                random.NextBytes(record);
                if (targetIndices.Contains(i))
                    knownTargets.Add((byte[])record.Clone());
                flat.Write(record);
                indexer.Add(record);
            }
        }
        Log($"- Generation (flat file + index): {clock.Elapsed.TotalSeconds:N2} s");

        var lookups = new byte[o.Lookups][];
        for (int i = 0; i < lookups.Length; i++)
        {
            if (random.NextDouble() > 0.5)
            {
                lookups[i] = knownTargets[random.Next(knownTargets.Count)];
            }
            else
            {
                lookups[i] = new byte[o.RecordSize];
                random.NextBytes(lookups[i]);
            }
        }

        // Phase 2: indexed lookups, all of them.
        clock.Restart();
        int hits = 0;
        foreach (byte[] item in lookups)
        {
            if (indexer.Find(item))
                hits++;
        }
        double indexSeconds = clock.Elapsed.TotalSeconds;

        // Phase 3: a few full scans, projected to the full lookup count.
        clock.Restart();
        int linearHits = 0;
        for (int i = 0; i < o.LinearSamples; i++)
        {
            if (FlatFileScanner.Contains(flatFile, lookups[i], o.RecordSize, o.BatchRecords))
                linearHits++;
        }
        double linearAverage = clock.Elapsed.TotalSeconds / o.LinearSamples;
        double projected = linearAverage * o.Lookups;

        int expectedLinearHits = lookups.Take(o.LinearSamples).Count(l => knownTargets.Contains(l));
        if (linearHits != expectedLinearHits)
            throw new InvalidOperationException($"Linear scan found {linearHits} hits, expected {expectedLinearHits}.");

        Log("");
        Log("| Method | Total | Per lookup |");
        Log("|---|--:|--:|");
        Log($"| Indexed (actual, {o.Lookups:N0} lookups, {hits:N0} hits) | {indexSeconds:N2} s | {indexSeconds / o.Lookups * 1e3:N4} ms |");
        Log($"| Linear batched scan (projected from {o.LinearSamples}) | {projected:N0} s ({projected / 3600:N2} h) | {linearAverage * 1e3:N1} ms |");
        Log("");
        Log($"Speed-up: **{projected / indexSeconds:N0}x**");
        Log("");
        Log("Note: at this size both files fit in the OS page cache, so this mostly measures cached reads rather than the physical disk.");

        if (!o.Keep)
            Directory.Delete(o.Directory, recursive: true);
        return sb.ToString();
    }

    /// <summary>Only ever wipes a directory this benchmark created (it carries a marker file).</summary>
    private static void PrepareDirectory(string dir)
    {
        if (Directory.Exists(dir))
        {
            if (!File.Exists(Path.Combine(dir, MarkerFile)))
            {
                throw new InvalidOperationException(
                    $"'{dir}' already exists and was not created by this benchmark; pass a different --disk-dir.");
            }
            Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, MarkerFile), "Created by Lookuptables.Bench; safe to delete.");
    }
}
