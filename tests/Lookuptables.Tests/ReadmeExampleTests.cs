using Lookuptables;

namespace Lookuptables.Tests;

/// <summary>Keeps the "Using the library" snippet in README.md compiling and correct.</summary>
public class ReadmeExampleTests
{
    [Fact]
    public void ReadmeExample()
    {
        byte[] record = [0x1F, 0xA0, 0x07, 0x42, 0x99];

        // Index binary records by their first 2 bytes (256 * 256 = 65,536 buckets).
        var table = new FlatTableBuckets<byte[], ByteKey>(depth: 2);

        bool added = table.AddUnique(record);   // false if it was already there
        bool found = table.Contains(record);

        // Everything that starts with 1F A0 07.
        var matches = new List<byte[]>();
        table.CollectPrefix([0x1F, 0xA0, 0x07], matches);

        // A-Z strings work the same way, with 26 symbols per layer.
        var words = new FlatTableBuckets<string, UpperAlphaKey>(depth: 3);
        words.AddUnique("HELLO");
        words.AddUnique("HELP");
        var hel = new List<string>();
        words.CollectPrefix("HEL", hel);

        Assert.True(added);
        Assert.True(found);
        Assert.Single(matches);
        Assert.Equal(["HELLO", "HELP"], hel.Order());
    }
}
