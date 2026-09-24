using System.Buffers;

namespace Lookuptables;

/// <summary>
/// Port of <c>DiskIndexer</c> from BenchDIsk.py / BenchDisk2.py. Fixed-size records are appended to
/// <c>root/b0/b1/bucket_b2.bin</c> (the first three bytes in hex), so a lookup only reads one small file.
/// </summary>
public sealed class DiskIndexer
{
    private readonly string _root;
    private readonly int _recordSize;

    public DiskIndexer(string root, int recordSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSize, 3);
        _root = root;
        _recordSize = recordSize;
        Directory.CreateDirectory(_root);
    }

    public int RecordSize => _recordSize;

    public string GetBucketPath(ReadOnlySpan<byte> record, out string folder)
    {
        folder = Path.Combine(_root, record[0].ToString("x2"), record[1].ToString("x2"));
        return Path.Combine(folder, $"bucket_{record[2]:x2}.bin");
    }

    public void Add(ReadOnlySpan<byte> record)
    {
        EnsureRecordSize(record);
        string path = GetBucketPath(record, out string folder);
        Directory.CreateDirectory(folder);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(record);
    }

    public bool Find(ReadOnlySpan<byte> record)
    {
        EnsureRecordSize(record);
        string path = GetBucketPath(record, out _);
        if (!File.Exists(path))
            return false;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_recordSize);
        try
        {
            Span<byte> current = buffer.AsSpan(0, _recordSize);
            while (stream.ReadAtLeast(current, _recordSize, throwOnEndOfStream: false) == _recordSize)
            {
                if (current.SequenceEqual(record))
                    return true;
            }
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void EnsureRecordSize(ReadOnlySpan<byte> record)
    {
        if (record.Length != _recordSize)
            throw new ArgumentException($"Record is {record.Length} bytes, expected {_recordSize}.", nameof(record));
    }
}

/// <summary>
/// The control group: a batched linear scan of one flat file (<c>linear_disk_search_batched</c>).
/// Unlike the Python version, which searches for the target anywhere in the batch (<c>target in chunk_batch</c>),
/// this compares record-aligned slices only, so it cannot report a match that straddles two records.
/// </summary>
public static class FlatFileScanner
{
    public static bool Contains(string path, ReadOnlySpan<byte> target, int recordSize, int batchRecords)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchRecords, 1);
        if (target.Length != recordSize)
            throw new ArgumentException($"Target is {target.Length} bytes, expected {recordSize}.", nameof(target));

        int batchBytes = checked(recordSize * batchRecords);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(batchBytes);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 0, FileOptions.SequentialScan);
            int read;
            while ((read = stream.ReadAtLeast(buffer.AsSpan(0, batchBytes), batchBytes, throwOnEndOfStream: false)) > 0)
            {
                for (int offset = 0; offset + recordSize <= read; offset += recordSize)
                {
                    if (buffer.AsSpan(offset, recordSize).SequenceEqual(target))
                        return true;
                }
            }
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
