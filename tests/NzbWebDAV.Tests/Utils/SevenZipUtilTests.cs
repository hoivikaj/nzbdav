using System.Text;
using NzbWebDAV.Utils;
using SharpCompress.Common;

namespace NzbWebDAV.Tests.Utils;

public class SevenZipUtilTests
{
    private const string StoredArchiveBase64 =
        "N3q8ryccAARez7gsaAAAAAAAAAAUAAAAAAAAAJeoNbBzdG9yZWQtc2V2ZW56aXAtZW50cnkBAE4BBAYAAQkVAAcLAQABAQAMFQAICgHuw9/ZAAAFARkBABEXAHMAYQBtAHAAbABlAC4AdAB4AHQAAAAUCgEAwGcQ9pEQ3QEVBgEAIICAgQAAABcGFQEJUwAHCwEAASEhARgMTwAA";

    [Fact]
    public void GetSevenZipEntries_ReturnsStoredEntryByteRange()
    {
        var archiveBytes = Convert.FromBase64String(StoredArchiveBase64);
        var expectedEntryBytes = Encoding.UTF8.GetBytes("stored-sevenzip-entry");
        using var archiveStream = new MemoryStream(archiveBytes);

        var entry = Assert.Single(SevenZipUtil.GetSevenZipEntries(archiveStream));

        Assert.Equal("sample.txt", entry.PathWithinArchive);
        Assert.Equal(CompressionType.None, entry.CompressionType);
        Assert.Equal(entry.FolderStartByteOffset, entry.ByteRangeWithinArchive.StartInclusive);

        var byteRange = entry.ByteRangeWithinArchive;
        var storedEntryBytes = archiveBytes
            .AsSpan(
                checked((int)byteRange.StartInclusive),
                checked((int)(byteRange.EndExclusive - byteRange.StartInclusive))
            )
            .ToArray();

        Assert.Equal(expectedEntryBytes, storedEntryBytes);
        Assert.True(archiveStream.CanRead);
    }

    [Fact]
    public void GetSevenZipEntries_ThrowsInvalidFormatException_OnCorruptSignature()
    {
        // Import must fail as a managed InvalidFormatException so the queue marks
        // the item failed instead of crashing the process (audit F3 / #477).
        var archiveBytes = Convert.FromBase64String(StoredArchiveBase64);
        archiveBytes[0] ^= 0xFF;
        using var archiveStream = new MemoryStream(archiveBytes);

        Assert.Throws<InvalidFormatException>(() =>
            SevenZipUtil.GetSevenZipEntries(archiveStream));
    }
}
