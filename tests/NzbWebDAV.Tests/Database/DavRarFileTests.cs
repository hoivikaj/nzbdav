using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Database;

public class DavRarFileTests
{
    [Fact]
    public void ToDavMultipartFileMeta_HealsUnderestimatedLegacyPartSize()
    {
        var legacy = new DavRarFile
        {
            RarParts =
            [
                new DavRarFile.RarPart
                {
                    SegmentIds = ["segment"],
                    PartSize = 100,
                    Offset = 20,
                    ByteCount = 100,
                }
            ],
        };

        var part = Assert.Single(legacy.ToDavMultipartFileMeta().FileParts);

        Assert.True(part.SegmentIdByteRange.Contains(part.FilePartByteRange));
        Assert.Equal(120, part.SegmentIdByteRange.Count);
    }
}
