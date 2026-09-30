using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Lookuptables;
using Microsoft.Win32.SafeHandles;

namespace Lookuptables.Bench;

public sealed record DiskOptions(string Directory, int Records, int RecordSize, int Lookups, int LinearSamples, int BatchRecords, bool Keep, int Seed,
    bool DirectoryIndex = true);

/// <summary>
/// Port of BenchDisk2.py, extended. Random 64 KB records go into one flat file, then three indexes are built
/// over it and compared with linear scans of the file:
/// <list type="bullet">
/// <item>the Python design: a directory tree with one bucket file per 3-byte prefix (a second copy of the data);</item>
/// <item>a burst trie in RAM mapping each record's first 16 bytes to its record number, then one read from the flat file;</item>
/// <item>the same with a plain <see cref="Dictionary{TKey, TValue}"/>, as the built-in baseline.</item>
/// </list>
/// The linear scan is measured two ways: one full pass per lookup (the Python script's comparison), and a single
/// pass that answers every lookup at once, which is how a scan would really be used for a batch of lookups.
/// </summary>
/// <remarks>
/// Record contents are a pure function of the record number (<see cref="RecordSource"/>), so hits can target any
/// record on disk without holding records in memory. Each method gets its own lookups, so none of them reads a
/// record another method has already pulled into the OS file cache.
/// </remarks>
public static class DiskBenchmark
{
    private const string MarkerFile = ".lookuptables-disk-bench";
    private const int PrefixBytes = 16;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(5);

    public static string Run(DiskOptions o)
    {
        long totalBytes = (long)o.Records * o.RecordSize;
        EnsureFreeSpace(o.Directory, totalBytes, o.DirectoryIndex);
        PrepareDirectory(o.Directory);

        string flatFile = Path.Combine(o.Directory, "huge_flat_file.bin");
        var indexer = new DiskIndexer(Path.Combine(o.Directory, "my_database_index"), o.RecordSize);
        long ramBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var sb = new StringBuilder();

        void Log(string line)
        {
            Console.WriteLine(line);
            sb.AppendLine(line);
        }

        Log("## Disk index (BenchDisk2.py)");
        Log("");
        long onDisk = o.DirectoryIndex ? 2 * totalBytes : totalBytes;
        Log($"- Records: {o.Records:N0} x {o.RecordSize:N0} bytes = {Gb(totalBytes):N2} GB flat file" +
            (o.DirectoryIndex ? $", plus {Gb(totalBytes):N2} GB for the directory index's copy" : " (directory index not built in this run)"));
        Log($"- Machine RAM: {Gb(ramBytes):N1} GB; the data on disk is {(double)onDisk / ramBytes:N1}x RAM");
        Log($"- Lookups: {o.Lookups:N0} per method, 50% hits spread over all records; each method gets its own lookups");

        // Phase 1: the flat file, written sequentially.
        Console.WriteLine();
        byte[] record = new byte[o.RecordSize];
        double flatSeconds = ForEachRecord("flat file", o, i =>
        {
            RecordSource.Fill(record, i, o.Seed);
            return record;
        }, out _, flatFileWriter: flatFile);
        Log($"- Flat file written in {flatSeconds:N0} s ({totalBytes / flatSeconds / (1 << 20):N0} MB/s)");

        // Phase 2: the Python-style directory index (one small file per 3-byte prefix).
        double directorySeconds = 0;
        if (o.DirectoryIndex)
        {
            directorySeconds = ForEachRecord("directory index", o, i =>
            {
                RecordSource.Fill(record, i, o.Seed);
                indexer.Add(record);
                return record;
            }, out _, flatFileWriter: null);
            Log($"- Directory index built in {directorySeconds:N0} s ({totalBytes / directorySeconds / (1 << 20):N0} MB/s)");
        }

        // Phase 3: the in-RAM indexes. Only the first 16 bytes of each record are needed to build them.
        (BurstTrie<byte[], ByteKey> trie, double trieSeconds, long trieBytes) = MeasureBuild(() => BuildTrie(o));
        Log($"- Burst trie index built in {trieSeconds:N1} s, {trieBytes / (double)(1 << 20):N0} MB of RAM ({trie.Shape})");
        (PrefixDictionary dictionary, double dictionarySeconds, long dictionaryBytes) = MeasureBuild(() => BuildDictionary(o));
        Log($"- Dictionary index built in {dictionarySeconds:N1} s, {dictionaryBytes / (double)(1 << 20):N0} MB of RAM");

        // Phase 4: indexed lookups, each method on its own lookups.
        Console.WriteLine();
        using SafeFileHandle flatHandle = File.OpenHandle(flatFile, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        byte[] stored = new byte[o.RecordSize];
        byte[] prefix = new byte[PrefixBytes];
        var candidates = new List<byte[]>();

        double directoryLookupSeconds = o.DirectoryIndex
            ? TimeLookups("directory index", o, Lookups(o, 1), target => indexer.Find(target))
            : double.NaN;

        double trieLookupSeconds = TimeLookups("burst trie", o, Lookups(o, 2), target =>
        {
            target.AsSpan(0, PrefixBytes).CopyTo(prefix);
            candidates.Clear();
            trie.CollectPrefix(prefix, candidates);
            foreach (byte[] key in candidates)
            {
                if (ReadRecord(flatHandle, BinaryPrimitives.ReadInt64BigEndian(key.AsSpan(PrefixBytes)), stored) && stored.AsSpan().SequenceEqual(target))
                    return true;
            }
            return false;
        });

        double dictionaryLookupSeconds = TimeLookups("dictionary", o, Lookups(o, 3), target =>
        {
            foreach (long recordNumber in dictionary.Find(target))
            {
                if (ReadRecord(flatHandle, recordNumber, stored) && stored.AsSpan().SequenceEqual(target))
                    return true;
            }
            return false;
        });

        // Phase 5: one sequential pass that answers every lookup at once.
        Console.WriteLine();
        long[] batchLookups = Lookups(o, 4);
        var wanted = new Dictionary<UInt128, List<int>>(batchLookups.Length);
        for (int i = 0; i < batchLookups.Length; i++)
        {
            RecordSource.Fill(prefix, batchLookups[i], o.Seed);
            UInt128 key = BinaryPrimitives.ReadUInt128BigEndian(prefix);
            if (!wanted.TryGetValue(key, out List<int>? positions))
                wanted[key] = positions = [];
            positions.Add(i);
        }
        byte[] candidate = new byte[o.RecordSize];
        int batchHits = 0;
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        long batchBytes = FlatFileScanner.Scan(flatFile, o.RecordSize, o.BatchRecords, (scanned, number) =>
        {
            if (wanted.TryGetValue(BinaryPrimitives.ReadUInt128BigEndian(scanned), out List<int>? positions))
            {
                foreach (int i in positions)
                {
                    RecordSource.Fill(candidate, batchLookups[i], o.Seed);
                    if (scanned.SequenceEqual(candidate))
                        batchHits++;
                }
            }
            if (clock.Elapsed - lastReport >= ProgressInterval)
            {
                lastReport = clock.Elapsed;
                Console.WriteLine($"  single-pass scan: {Gb((number + 1) * o.RecordSize),5:N1} GB read, {batchHits:N0} hits so far");
            }
            return true;
        });
        double batchSeconds = clock.Elapsed.TotalSeconds;
        CheckHits("single-pass scan", batchLookups, o, batchHits);
        Console.WriteLine($"  single-pass scan: {Gb(batchBytes):N1} GB in {batchSeconds:N1} s ({batchBytes / batchSeconds / (1 << 20):N0} MB/s)");

        // Phase 6: one full pass per lookup, sampled and projected (the Python script's comparison).
        Console.WriteLine();
        long[] sampleLookups = Lookups(o, 1);
        double linearSeconds = 0;
        long linearBytes = 0;
        for (int i = 0; i < o.LinearSamples; i++)
        {
            byte[] target = new byte[o.RecordSize];
            RecordSource.Fill(target, sampleLookups[i], o.Seed);
            bool expected = sampleLookups[i] < o.Records;
            long t0 = Stopwatch.GetTimestamp();
            bool found = FlatFileScanner.Contains(flatFile, target, o.RecordSize, o.BatchRecords);
            double seconds = Stopwatch.GetElapsedTime(t0).TotalSeconds;
            if (found != expected)
                throw new InvalidOperationException($"Linear scan {i + 1}: found = {found}, expected {expected}.");

            long scanned = expected ? (sampleLookups[i] + 1) * o.RecordSize : totalBytes;
            linearSeconds += seconds;
            linearBytes += scanned;
            Console.WriteLine($"  one pass per lookup, sample {i + 1}/{o.LinearSamples}: {(found ? "hit " : "miss")} after {Gb(scanned),5:N1} GB " +
                              $"in {seconds,5:N1} s ({scanned / seconds / (1 << 20):N0} MB/s)");
        }
        double perLookupScan = linearSeconds / o.LinearSamples;
        double projected = perLookupScan * o.Lookups;
        Console.WriteLine();

        Log($"- Sequential read speed: {batchBytes / batchSeconds / (1 << 20):N0} MB/s (single pass), {linearBytes / linearSeconds / (1 << 20):N0} MB/s (per-lookup samples)");
        Log("");
        Log($"| Method | Index | All {o.Lookups:N0} lookups | Per lookup |");
        Log("|---|---|--:|--:|");
        Log($"| Linear scan, one full pass per lookup (projected from {o.LinearSamples}) | none | {projected:N0} s ({projected / 3600:N0} h) | {perLookupScan * 1e3:N0} ms |");
        Log($"| Linear scan, one pass answering every lookup at once | none | {batchSeconds:N1} s | {batchSeconds / o.Lookups * 1e3:N3} ms (average) |");
        if (o.DirectoryIndex)
            Log($"| Directory index (Python design: one file per 3-byte prefix) | {Gb(totalBytes):N0} GB on disk, {directorySeconds:N0} s to build | {directoryLookupSeconds:N1} s | {directoryLookupSeconds / o.Lookups * 1e3:N3} ms |");
        Log($"| Burst trie in RAM, then one read from the flat file | {trieBytes / (double)(1 << 20):N0} MB RAM, {trieSeconds:N1} s to build | {trieLookupSeconds:N1} s | {trieLookupSeconds / o.Lookups * 1e3:N3} ms |");
        Log($"| `Dictionary<UInt128, long>` in RAM, then one read from the flat file | {dictionaryBytes / (double)(1 << 20):N0} MB RAM, {dictionarySeconds:N1} s to build | {dictionaryLookupSeconds:N1} s | {dictionaryLookupSeconds / o.Lookups * 1e3:N3} ms |");
        Log("");
        var indexes = new List<(string Name, double Seconds)> { ("the burst trie", trieLookupSeconds), ("the dictionary", dictionaryLookupSeconds) };
        if (o.DirectoryIndex)
            indexes.Add(("the directory index", directoryLookupSeconds));
        Log($"Answering all {o.Lookups:N0} lookups, compared with the single-pass scan: " +
            string.Join(", ", indexes.Select(x => $"{x.Name} is {batchSeconds / x.Seconds:N1}x faster")) +
            ". Compared with one pass per lookup: " +
            string.Join(", ", indexes.Select(x => $"{x.Name} is {projected / x.Seconds:N0}x faster")) + ".");
        Log("");
        Log(2 * totalBytes > ramBytes
            ? "The data is larger than RAM, so most reads come from the SSD rather than the OS file cache " +
              "(recently written data can still be cached, so a small share of lookups may be served from RAM)."
            : "Note: the data fits in RAM, so this mostly measures reads from the OS file cache rather than the SSD.");

        if (!o.Keep)
        {
            Console.WriteLine("Deleting generated files...");
            flatHandle.Dispose();
            Directory.Delete(o.Directory, recursive: true);
        }
        return sb.ToString();
    }

    /// <summary>Half hits (any stored record), half misses (record numbers past the end). One independent stream per method.</summary>
    private static long[] Lookups(DiskOptions o, int stream)
    {
        var random = new Random(o.Seed * 31 + stream);
        long[] lookups = new long[o.Lookups];
        for (int i = 0; i < lookups.Length; i++)
            lookups[i] = random.NextDouble() > 0.5 ? random.Next(o.Records) : o.Records + random.NextInt64(int.MaxValue);
        return lookups;
    }

    /// <summary>Times <paramref name="find"/> over the lookups; the records to look for are generated in untimed batches.</summary>
    private static double TimeLookups(string label, DiskOptions o, long[] lookups, Func<byte[], bool> find)
    {
        const int GenerateBatch = 256;
        var batch = new byte[GenerateBatch][];
        for (int i = 0; i < batch.Length; i++)
            batch[i] = new byte[o.RecordSize];

        long ticks = 0;
        int hits = 0;
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        for (int start = 0; start < lookups.Length; start += GenerateBatch)
        {
            int count = Math.Min(GenerateBatch, lookups.Length - start);
            for (int j = 0; j < count; j++)
                RecordSource.Fill(batch[j], lookups[start + j], o.Seed);

            long t0 = Stopwatch.GetTimestamp();
            for (int j = 0; j < count; j++)
            {
                if (find(batch[j]))
                    hits++;
            }
            ticks += Stopwatch.GetTimestamp() - t0;

            if (clock.Elapsed - lastReport >= ProgressInterval)
            {
                lastReport = clock.Elapsed;
                Console.WriteLine($"  {label}: {start + count,9:N0} / {lookups.Length:N0} lookups");
            }
        }
        CheckHits(label, lookups, o, hits);
        double seconds = (double)ticks / Stopwatch.Frequency;
        Console.WriteLine($"  {label}: {seconds:N1} s, {seconds / lookups.Length * 1e3:N3} ms per lookup");
        return seconds;
    }

    private static void CheckHits(string label, long[] lookups, DiskOptions o, int hits)
    {
        int expected = lookups.Count(l => l < o.Records);
        if (hits != expected)
            throw new InvalidOperationException($"{label} found {hits} hits, expected {expected}.");
    }

    /// <summary>Runs <paramref name="produce"/> for every record with progress output; optionally streams the records to a file.</summary>
    private static double ForEachRecord(string label, DiskOptions o, Func<int, byte[]> produce, out long bytes, string? flatFileWriter)
    {
        using FileStream? flat = flatFileWriter is null
            ? null
            : new FileStream(flatFileWriter, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        flat?.SetLength((long)o.Records * o.RecordSize);

        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        bytes = 0;
        for (int i = 0; i < o.Records; i++)
        {
            byte[] record = produce(i);
            flat?.Write(record);
            bytes += record.Length;
            if (clock.Elapsed - lastReport >= ProgressInterval)
            {
                lastReport = clock.Elapsed;
                Console.WriteLine($"  {label}: {i + 1,11:N0} / {o.Records:N0} records  {Gb(bytes),6:N1} GB  " +
                                  $"{bytes / clock.Elapsed.TotalSeconds / (1 << 20),6:N0} MB/s  " +
                                  $"ETA {TimeSpan.FromSeconds(clock.Elapsed.TotalSeconds * (o.Records - i - 1) / (i + 1)):hh\\:mm\\:ss}");
            }
        }
        flat?.Flush(flushToDisk: true);
        return clock.Elapsed.TotalSeconds;
    }

    private static (T Index, double Seconds, long Bytes) MeasureBuild<T>(Func<T> build)
    {
        long before = GC.GetTotalMemory(forceFullCollection: true);
        long start = Stopwatch.GetTimestamp();
        T index = build();
        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        long bytes = GC.GetTotalMemory(forceFullCollection: true) - before;
        return (index, seconds, bytes);
    }

    /// <summary>Keys are the record's first 16 bytes followed by its record number (big-endian), 24 bytes in all.</summary>
    private static BurstTrie<byte[], ByteKey> BuildTrie(DiskOptions o)
    {
        var keys = new byte[o.Records][];
        for (int i = 0; i < o.Records; i++)
        {
            var key = new byte[PrefixBytes + sizeof(long)];
            RecordSource.Fill(key.AsSpan(0, PrefixBytes), i, o.Seed);
            BinaryPrimitives.WriteInt64BigEndian(key.AsSpan(PrefixBytes), i);
            keys[i] = key;
        }
        var trie = new BurstTrie<byte[], ByteKey>();
        trie.LoadDistinct(keys);
        return trie;
    }

    private static PrefixDictionary BuildDictionary(DiskOptions o)
    {
        var dictionary = new PrefixDictionary(o.Records);
        Span<byte> prefix = stackalloc byte[PrefixBytes];
        for (int i = 0; i < o.Records; i++)
        {
            RecordSource.Fill(prefix, i, o.Seed);
            dictionary.Add(prefix, i);
        }
        return dictionary;
    }

    private static bool ReadRecord(SafeFileHandle handle, long recordNumber, byte[] buffer)
    {
        long offset = recordNumber * buffer.Length;
        int total = 0;
        while (total < buffer.Length)
        {
            int read = RandomAccess.Read(handle, buffer.AsSpan(total), offset + total);
            if (read == 0)
                return false;
            total += read;
        }
        return true;
    }

    private static double Gb(long bytes) => bytes / (double)(1L << 30);

    private static void EnsureFreeSpace(string dir, long totalBytes, bool directoryIndex)
    {
        // Flat file, plus the directory index (each record rounds up to whole 4 KB clusters), plus 5 GB headroom.
        long needed = totalBytes + (directoryIndex ? (long)Math.Ceiling(totalBytes / 4096.0) * 4096 : 0) + (5L << 30);
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

    /// <summary>Record numbers by their first 16 bytes; the rare duplicate prefix goes to an overflow list.</summary>
    private sealed class PrefixDictionary(int capacity)
    {
        private readonly Dictionary<UInt128, long> _first = new(capacity);
        private readonly Dictionary<UInt128, List<long>> _more = [];

        public void Add(ReadOnlySpan<byte> prefix, long recordNumber)
        {
            UInt128 key = BinaryPrimitives.ReadUInt128BigEndian(prefix);
            if (_first.TryAdd(key, recordNumber))
                return;
            if (!_more.TryGetValue(key, out List<long>? list))
                _more[key] = list = [];
            list.Add(recordNumber);
        }

        public IEnumerable<long> Find(ReadOnlySpan<byte> record)
        {
            UInt128 key = BinaryPrimitives.ReadUInt128BigEndian(record);
            if (!_first.TryGetValue(key, out long first))
                return [];
            return _more.TryGetValue(key, out List<long>? list) ? [first, .. list] : [first];
        }
    }
}

/// <summary>
/// Deterministic pseudo-random record contents (SplitMix64): record <c>n</c> can be regenerated at any time,
/// so lookups can target any record on disk without keeping records in memory. Filling a shorter span gives
/// the same leading bytes as the full record.
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
