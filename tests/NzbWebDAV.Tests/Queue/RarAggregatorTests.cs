using NzbWebDAV.Models;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Queue.FileAggregators;
using NzbWebDAV.Queue.FileProcessors;

namespace NzbWebDAV.Tests.Queue;

public class RarAggregatorTests
{
    [Fact]
    public void SortByPartNumber_NormalizesFilenameNumbersAgainstHeaders()
    {
        var second = Segment(headerPart: 1, filenamePart: 2, start: 5, length: 5);
        var first = Segment(headerPart: 0, filenamePart: 1, start: 0, length: 5);

        var sorted = RarAggregator.SortByPartNumber([second, first]);

        Assert.Equal(new[] { first, second }, sorted);
    }

    [Fact]
    public void SortByPartNumber_RejectsAmbiguousDuplicateParts()
    {
        var first = Segment(headerPart: 0, filenamePart: 1, start: 0, length: 5);
        var duplicate = Segment(headerPart: 0, filenamePart: 1, start: 5, length: 5);

        Assert.Throws<InvalidDataException>(
            () => RarAggregator.SortByPartNumber([first, duplicate]));
    }

    [Fact]
    public void SortByPartNumber_UsesFilenameWhenHeadersCollide()
    {
        var parts = Enumerable.Range(1, 5)
            .Select(i => Segment(headerPart: 0, filenamePart: i, start: (i - 1) * 2, length: 2))
            .ToList();

        var sorted = RarAggregator.SortByPartNumber(parts);

        Assert.Equal(5, sorted.Length);
        Assert.Equal([1, 2, 3, 4, 5], sorted.Select(x => x.PartNumber.PartNumberFromFilename).ToArray());
        Assert.Equal(RarAggregator.PartNumberSource.Filename,
            RarAggregator.SelectPartNumberSource(parts));
    }

    [Fact]
    public void SortByPartNumber_ClassicSetWithFirstVolumeMarker()
    {
        var first = SegmentNullable(headerPart: -1, filenamePart: -1, start: 0, length: 2);
        var r00 = SegmentNullable(headerPart: null, filenamePart: 0, start: 2, length: 2);
        var r01 = SegmentNullable(headerPart: null, filenamePart: 1, start: 4, length: 2);
        var r02 = SegmentNullable(headerPart: null, filenamePart: 2, start: 6, length: 2);
        var r03 = SegmentNullable(headerPart: null, filenamePart: 3, start: 8, length: 2);

        var sorted = RarAggregator.SortByPartNumber([r02, first, r03, r00, r01]);

        Assert.Equal([first, r00, r01, r02, r03], sorted);
    }

    [Fact]
    public void SortByPartNumber_DedupesIdenticalDuplicateVolumes()
    {
        var first = Segment(headerPart: 0, filenamePart: 1, start: 0, length: 5);
        var duplicate = Segment(headerPart: 0, filenamePart: 1, start: 0, length: 5);
        var second = Segment(headerPart: 1, filenamePart: 2, start: 5, length: 5);

        var sorted = RarAggregator.SortByPartNumber([first, duplicate, second]);

        Assert.Equal(2, sorted.Length);
        Assert.Equal([first, second], sorted);
    }

    [Fact]
    public void SortByPartNumber_RejectsEqualMetadataVolumesWithDifferentMessageIds()
    {
        var first = SegmentNullable(headerPart: 0, filenamePart: 1, start: 0, length: 5,
            messageId: "vol-a@example.com");
        var other = SegmentNullable(headerPart: 0, filenamePart: 1, start: 0, length: 5,
            messageId: "vol-b@example.com");

        var ex = Assert.Throws<InvalidDataException>(
            () => RarAggregator.SortByPartNumber([first, other]));
        Assert.Contains("duplicate volume numbers", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SortByPartNumber_DistinctHeadersUnchanged()
    {
        var parts = Enumerable.Range(0, 3)
            .Select(i => Segment(headerPart: i, filenamePart: i + 1, start: i * 3, length: 3))
            .Reverse()
            .ToList();

        var sorted = RarAggregator.SortByPartNumber(parts);

        Assert.Equal([0, 1, 2], sorted.Select(x => x.PartNumber.PartNumberFromHeader).ToArray());
        Assert.Equal(RarAggregator.PartNumberSource.Header,
            RarAggregator.SelectPartNumberSource(parts));
    }

    [Fact]
    public void ValidateVolumes_RejectsMissingData()
    {
        var segment = Segment(headerPart: 0, filenamePart: 1, start: 0, length: 10);
        segment = new RarProcessor.StoredFileSegment
        {
            NzbFile = segment.NzbFile,
            PartSize = segment.PartSize,
            ArchiveName = segment.ArchiveName,
            PartNumber = segment.PartNumber,
            ReleaseDate = segment.ReleaseDate,
            PathWithinArchive = segment.PathWithinArchive,
            ByteRangeWithinPart = segment.ByteRangeWithinPart,
            AesParams = null,
            FileUncompressedSize = 100
        };

        Assert.Throws<InvalidDataException>(
            () => RarAggregator.ValidateVolumes([segment]));
    }

    private static RarProcessor.StoredFileSegment Segment(
        int headerPart, int filenamePart, long start, long length)
        => SegmentNullable(headerPart, filenamePart, start, length);

    private static RarProcessor.StoredFileSegment SegmentNullable(
        int? headerPart, int? filenamePart, long start, long length,
        string messageId = "shared@example.com")
    {
        return new RarProcessor.StoredFileSegment
        {
            NzbFile = new NzbFile
            {
                Subject = "archive",
                Segments =
                {
                    new NzbSegment { MessageId = messageId, Bytes = length }
                },
            },
            PartSize = length,
            ArchiveName = "archive",
            PartNumber = new RarProcessor.PartNumber
            {
                PartNumberFromHeader = headerPart,
                PartNumberFromFilename = filenamePart
            },
            ReleaseDate = DateTimeOffset.UnixEpoch,
            PathWithinArchive = "movie.mkv",
            ByteRangeWithinPart = LongRange.FromStartAndSize(start, length),
            AesParams = null,
            FileUncompressedSize = 10
        };
    }
}
