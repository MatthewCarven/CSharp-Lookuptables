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

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1000)]
    public void Scan_VisitsEveryRecordInOrderAndCanStopEarly(int batchRecords)
    {
        Directory.CreateDirectory(_dir);
        string flatFile = Path.Combine(_dir, "scan.bin");
        const int records = 50;
        using (var flat = File.Create(flatFile))
        {
            for (int i = 0; i < records; i++)
            {
                var record = new byte[RecordSize];
                record[0] = (byte)i;
                flat.Write(record);
            }
        }

        var seen = new List<long>();
        long bytes = FlatFileScanner.Scan(flatFile, RecordSize, batchRecords, (record, number) =>
        {
            Assert.Equal((byte)number, record[0]);
            seen.Add(number);
            return true;
        });
        Assert.Equal(Enumerable.Range(0, records).Select(i => (long)i), seen);
        Assert.Equal(records * RecordSize, bytes);

        int visited = 0;
        FlatFileScanner.Scan(flatFile, RecordSize, batchRecords, (_, number) =>
        {
            visited++;
            return number < 20;
        });
        Assert.Equal(21, visited);
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
