using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Models;
using NzbWebDAV.Streams;
using NzbWebDAV.Tests.Fakes;
using NzbWebDAV.Tests.TestUtils;
using UsenetSharp.Streams;

namespace NzbWebDAV.Tests.Streams;

public class DavMultipartFileStreamTests
{
    [Fact]
    public void GetEffectivePartLength_UsesPackedRangeEndForUnderestimatedVolume()
    {
        var part = MultipartFile(
            segmentRange: LongRange.FromStartAndSize(0, 8),
            fileRange: LongRange.FromStartAndSize(4, 12))
            .Metadata.FileParts[0];

        Assert.Equal(16, DavMultipartFileStream.GetEffectivePartLength(part));
    }

    [SkippableFact]
    public async Task ReadAsync_HealsUnderestimatedVolumeLength()
    {
        Skip.IfNot(RapidYenc.IsAvailable, "rapidyenc native library not available on this platform");

        var volumeBytes = Enumerable.Range(0, 16).Select(x => (byte)x).ToArray();
        using var client = new FakeNntpClient(new Dictionary<string, byte[]>
        {
            ["segment"] = volumeBytes,
        });
        var multipart = MultipartFile(
            segmentRange: LongRange.FromStartAndSize(0, 8),
            fileRange: LongRange.FromStartAndSize(4, 12));
        await using var stream = new DavMultipartFileStream(
            multipart,
            client,
            articleBufferSize: 0,
            resolver: null,
            usePipelinedBodyRequests: false,
            fileName: "movie.mkv");

        var buffer = new byte[12];
        var bytesRead = await stream.ReadAsync(buffer);

        Assert.Equal(buffer.Length, bytesRead);
        Assert.Equal(volumeBytes[4..], buffer);
    }

    [Fact]
    public async Task ReadAsync_InvalidSegmentRangeThrowsKnownSeekError()
    {
        using var client = new FakeNntpClient(new Dictionary<string, byte[]>());
        var multipart = MultipartFile(
            segmentRange: LongRange.FromStartAndSize(1, 8),
            fileRange: LongRange.FromStartAndSize(4, 4));
        await using var stream = new DavMultipartFileStream(
            multipart,
            client,
            articleBufferSize: 0,
            resolver: null,
            usePipelinedBodyRequests: false,
            fileName: "movie.mkv");

        await Assert.ThrowsAsync<SeekPositionNotFoundException>(
            () => stream.ReadAsync(new byte[1], 0, 1));
    }

    private static DavMultipartFile MultipartFile(LongRange segmentRange, LongRange fileRange) =>
        new()
        {
            Id = Guid.NewGuid(),
            Metadata = new DavMultipartFile.Meta
            {
                FileParts =
                [
                    new DavMultipartFile.FilePart
                    {
                        SegmentIds = ["segment"],
                        SegmentIdByteRange = segmentRange,
                        FilePartByteRange = fileRange,
                    }
                ],
            },
        };
}
