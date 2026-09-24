using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Lookuptables;

namespace Lookuptables.Bench;

public sealed record DiskOptions(string Directory, int Records, int RecordSize, int Lookups, int LinearSamples, int BatchRecords, bool Keep, int Seed);

/// <summary>
/// Port of BenchDisk2.py: stream random records into both a flat file and the 3-byte directory index,
/// then compare indexed lookups with batched full-file scans (projected, as in the Python script).
/// </summary>
/// <remarks>
/// Unlike the Python script, which keeps ~100 records and looks them up over and over (so every hit after
/// the first comes from the OS file cache), record contents here are a pure function of the record number.
/// Hits are spread across every record on disk and nothing has to be held in memory.
/// </remarks>
public static class DiskBenchmark
{
    private const string MarkerFile = ".lookuptables-disk-bench";
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(5);

    public static string Run(DiskOptions o)
    {
        long totalBytes = (long)o.Records * o.RecordSize;
        EnsureFreeSpace(o.Directory, totalBytes);
        PrepareDirectory(o.Directory);

        var indexer = new DiskIndexer(Path.Combine(o.Directory, "my_database_index"), o.RecordSize);
        string flatFile = Path.Combine(o.Directory, "huge_flat_file.bin");
        long ramBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var sb = new StringBuilder();

        void Log(string line)
        {
            Console.WriteLine(line);
            sb.AppendLine(line);
        }

        Log("## Disk index (BenchDisk2.py)");
        Log("");
        Log($"- Records: {o.Records:N0} x {o.RecordSize:N0} bytes = {Gb(totalBytes):N2} GB flat file + {Gb(totalBytes):N2} GB index");
        Log($"- Machine RAM: {Gb(ramBytes):N1} GB; the database is {2.0 * totalBytes / ramBytes:N1}x RAM");
        Log($"- Lookups: {o.Lookups:N0} (50% hits, spread over all records); linear scan samples: {o.LinearSamples}; batch: {o.BatchRecords} records");

        // Phase 1: generation.
        byte[] record = new byte[o.RecordSize];
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        Console.WriteLine();
        using (var flat = new FileStream(flatFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            flat.SetLength(totalBytes);
            for (int i = 0; i < o.Records; i++)
            {
                RecordSource.Fill(record, i, o.Seed);
                flat.Write(record);
                indexer.Add(record);

                if (clock.Elapsed - lastReport >= ProgressInterval)
                {
                    lastReport = clock.Elapsed;
                    double written = 2.0 * (i + 1) * o.RecordSize;
                    Console.WriteLine($"  generated {i + 1,11:N0} / {o.Records:N0} records  {Gb((long)written),7:N1} GB written  " +
                                      $"{written / clock.Elapsed.TotalSeconds / (1 << 20),6:N0} MB/s  " +
                                      $"ETA {TimeSpan.FromSeconds(clock.Elapsed.TotalSeconds * (o.Records - i - 1) / (i + 1)):hh\\:mm\\:ss}");
                }
            }
        }
        double generationSeconds = clock.Elapsed.TotalSeconds;
        Log($"- Generation: {generationSeconds:N0} s ({2.0 * totalBytes / generationSeconds / (1 << 20):N0} MB/s written, flat file + index)");

        // Half hits (any stored record), half misses (record numbers past the end, never written).
        var random = new Random(o.Seed);
        long[] lookups = new long[o.Lookups];
        for (int i = 0; i < lookups.Length; i++)
            lookups[i] = random.NextDouble() > 0.5 ? random.Next(o.Records) : o.Records + random.NextInt64(int.MaxValue);

        // Phase 2: indexed lookups. Records are regenerated in untimed batches so only Find is measured.
        Console.WriteLine();
        const int GenerateBatch = 256;
        var batch = new byte[GenerateBatch][];
        for (int i = 0; i < batch.Length; i++)
            batch[i] = new byte[o.RecordSize];
        long indexTicks = 0;
        int hits = 0, expectedHits = 0;
        clock.Restart();
        lastReport = TimeSpan.Zero;
        for (int start = 0; start < lookups.Length; start += GenerateBatch)
        {
            int count = Math.Min(GenerateBatch, lookups.Length - start);
            for (int j = 0; j < count; j++)
                RecordSource.Fill(batch[j], lookups[start + j], o.Seed);

            long t0 = Stopwatch.GetTimestamp();
            for (int j = 0; j < count; j++)
            {
                if (indexer.Find(batch[j]))
                    hits++;
            }
            indexTicks += Stopwatch.GetTimestamp() - t0;

            for (int j = 0; j < count; j++)
            {
                if (lookups[start + j] < o.Records)
                    expectedHits++;
            }

            if (clock.Elapsed - lastReport >= ProgressInterval)
            {
                lastReport = clock.Elapsed;
                Console.WriteLine($"  indexed lookups {start + count,9:N0} / {o.Lookups:N0}");
            }
        }
        double indexSeconds = (double)indexTicks / Stopwatch.Frequency;
        if (hits != expectedHits)
            throw new InvalidOperationException($"Index found {hits} hits, expected {expectedHits}.");

        // Phase 3: a few full scans, projected to the full lookup count.
        Console.WriteLine();
        double linearSeconds = 0;
        long bytesScanned = 0;
        for (int i = 0; i < o.LinearSamples; i++)
        {
            RecordSource.Fill(record, lookups[i], o.Seed);
            bool expected = lookups[i] < o.Records;
            long t0 = Stopwatch.GetTimestamp();
            bool found = FlatFileScanner.Contains(flatFile, record, o.RecordSize, o.BatchRecords);
            double seconds = Stopwatch.GetElapsedTime(t0).TotalSeconds;
            if (found != expected)
                throw new InvalidOperationException($"Linear scan {i + 1}: found = {found}, expected {expected}.");

            long scanned = expected ? (lookups[i] + 1) * o.RecordSize : totalBytes;
            linearSeconds += seconds;
            bytesScanned += scanned;
            Console.WriteLine($"  linear scan {i + 1}/{o.LinearSamples}: {(found ? "hit " : "miss")} after {Gb(scanned),6:N1} GB " +
                              $"in {seconds,6:N1} s ({scanned / seconds / (1 << 20):N0} MB/s)");
        }
        double linearAverage = linearSeconds / o.LinearSamples;
        double projected = linearAverage * o.Lookups;
        Console.WriteLine();

        Log($"- Linear scan read speed: {bytesScanned / linearSeconds / (1 << 20):N0} MB/s");
        Log("");
        Log("| Method | Total | Per lookup |");
        Log("|---|--:|--:|");
        Log($"| Indexed (actual, {o.Lookups:N0} lookups, {hits:N0} hits) | {indexSeconds:N2} s | {indexSeconds / o.Lookups * 1e3:N4} ms |");
        Log($"| Linear batched scan (projected from {o.LinearSamples}) | {projected:N0} s ({projected / 3600:N2} h) | {linearAverage * 1e3:N1} ms |");
        Log("");
        Log($"Speed-up: **{projected / indexSeconds:N0}x**");
        Log("");
        Log(2 * totalBytes > ramBytes
            ? "The database is larger than RAM, so most reads come from the SSD rather than the OS file cache " +
              "(recently written data can still be cached, so a small share of lookups may be served from RAM)."
            : "Note: the database fits in RAM, so this mostly measures reads from the OS file cache rather than the SSD.");

        if (!o.Keep)
        {
            Console.WriteLine("Deleting generated files...");
            Directory.Delete(o.Directory, recursive: true);
        }
        return sb.ToString();
    }

    private static double Gb(long bytes) => bytes / (double)(1L << 30);

    private static void EnsureFreeSpace(string dir, long totalBytes)
    {
        // Flat file + index, where each record rounds up to whole 4 KB clusters, plus 5 GB headroom.
        long needed = totalBytes + (long)Math.Ceiling(totalBytes / 4096.0) * 4096 + (5L << 30);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!);
        if (drive.AvailableFreeSpace < needed)
        {
            throw new InvalidOperationException(
                $"Need about {Gb(needed):N0} GB free on {drive.Name}, only {Gb(drive.AvailableFreeSpace):N0} GB available.");
        }
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

/// <summary>
/// Deterministic pseudo-random record contents (SplitMix64): record <c>n</c> can be regenerated at any time,
/// so lookups can target any record on disk without keeping records in memory.
/// </summary>
public static class RecordSource
{
    public static void Fill(Span<byte> record, long index, int seed)
    {
        ulong state = (ulong)seed * 0x9E3779B97F4A7C15UL ^ (ulong)index * 0xD1B54A32D192ED03UL;
        int offset = 0;
        for (; offset + 8 <= record.Length; offset += 8)
            BinaryPrimitives.WriteUInt64LittleEndian(record[offset..], Next(ref state));
        if (offset < record.Length)
        {
            Span<byte> tail = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(tail, Next(ref state));
            tail[..(record.Length - offset)].CopyTo(record[offset..]);
        }
    }

    private static ulong Next(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
