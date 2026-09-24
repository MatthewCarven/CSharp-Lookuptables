using Lookuptables;

namespace Lookuptables.Tests;

public sealed class DiskIndexerTests : IDisposable
{
    private const int RecordSize = 64;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lookuptables-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void IndexAndFlatFile_AgreeOnHitsAndMisses()
    {
        var random = new Random(3);
        var indexer = new DiskIndexer(Path.Combine(_dir, "index"), RecordSize);
        string flatFile = Path.Combine(_dir, "flat.bin");

        var stored = new List<byte[]>();
        using (var flat = File.Create(flatFile))
        {
            for (int i = 0; i < 200; i++)
            {
                var record = new byte[RecordSize];
                random.NextBytes(record);
                record[0] = (byte)(i % 2); // force a few records to share bucket directories
                stored.Add(record);
                flat.Write(record);
                indexer.Add(record);
            }
        }

        foreach (byte[] record in stored)
        {
            Assert.True(indexer.Find(record));
            Assert.True(FlatFileScanner.Contains(flatFile, record, RecordSize, batchRecords: 7));
        }

        var missing = new byte[RecordSize];
        random.NextBytes(missing);
        Assert.False(indexer.Find(missing));
        Assert.False(FlatFileScanner.Contains(flatFile, missing, RecordSize, batchRecords: 7));
    }

    [Fact]
    public void BucketPath_UsesFirstThreeBytesInHex()
    {
        var indexer = new DiskIndexer(_dir, RecordSize);
        var record = new byte[RecordSize];
        record[0] = 0x0a; record[1] = 0xff; record[2] = 0x10;

        string path = indexer.GetBucketPath(record, out string folder);

        Assert.Equal(Path.Combine(_dir, "0a", "ff"), folder);
        Assert.Equal(Path.Combine(folder, "bucket_10.bin"), path);
    }
}
