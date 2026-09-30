using System.Buffers;
using Microsoft.Win32.SafeHandles;

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

/// <summary>Called for every record of a scan; return false to stop early.</summary>
public delegate bool RecordVisitor(ReadOnlySpan<byte> record, long recordNumber);

/// <summary>
/// The control group: a linear scan of one flat file (<c>linear_disk_search_batched</c>). It reads large batches
/// and fetches the next batch from disk while the current one is being checked, so the scan runs as close to the
/// drive's sequential read speed as it can. Unlike the Python version, which searches for the target anywhere in
/// the batch (<c>target in chunk_batch</c>), records are compared on record boundaries only, so a match can never
/// straddle two records.
/// </summary>
public static class FlatFileScanner
{
    /// <summary>One scan per target: stops at the first matching record.</summary>
    public static bool Contains(string path, byte[] target, int recordSize, int batchRecords)
    {
        if (target.Length != recordSize)
            throw new ArgumentException($"Target is {target.Length} bytes, expected {recordSize}.", nameof(target));
        bool found = false;
        Scan(path, recordSize, batchRecords, (record, _) =>
        {
            found = record.SequenceEqual(target);
            return !found;
        });
        return found;
    }

    /// <summary>Visits every record in file order until <paramref name="visitor"/> returns false.</summary>
    /// <returns>The number of bytes read.</returns>
    public static long Scan(string path, int recordSize, int batchRecords, RecordVisitor visitor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchRecords, 1);
        int batchBytes = checked(recordSize * batchRecords);
        // Declared first so it is disposed last, after any read-ahead has finished with it.
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        byte[] current = ArrayPool<byte>.Shared.Rent(batchBytes);
        byte[] next = ArrayPool<byte>.Shared.Rent(batchBytes);
        Task<int>? pending = null;
        try
        {
            long length = RandomAccess.GetLength(handle);
            long offset = 0, bytesRead = 0, recordNumber = 0;
            int read = ReadFull(handle, current, batchBytes, offset);
            while (read > 0)
            {
                bytesRead += read;
                long nextOffset = offset + read;
                if (nextOffset < length)
                {
                    byte[] target = next;
                    pending = Task.Run(() => ReadFull(handle, target, batchBytes, nextOffset));
                }

                for (int position = 0; position + recordSize <= read; position += recordSize, recordNumber++)
                {
                    if (!visitor(current.AsSpan(position, recordSize), recordNumber))
                        return bytesRead;
                }

                read = pending?.GetAwaiter().GetResult() ?? 0;
                pending = null;
                offset = nextOffset;
                (current, next) = (next, current);
            }
            return bytesRead;
        }
        finally
        {
            // An early stop can leave a read-ahead in flight into one of the buffers; let it land before recycling
            // them. Its result is not needed, so neither is any error it hit.
            try
            {
                pending?.Wait();
            }
            catch (AggregateException)
            {
            }
            ArrayPool<byte>.Shared.Return(current);
            ArrayPool<byte>.Shared.Return(next);
        }
    }

    private static int ReadFull(SafeFileHandle handle, byte[] buffer, int count, long offset)
    {
        int total = 0;
        while (total < count)
        {
            int read = RandomAccess.Read(handle, buffer.AsSpan(total, count - total), offset + total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}
