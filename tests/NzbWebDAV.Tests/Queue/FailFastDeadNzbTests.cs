using NzbWebDAV.Exceptions;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Queue;
using NzbWebDAV.Queue.DeobfuscationSteps._1.FetchFirstSegment;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;
using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Queue;

public class FailFastDeadNzbTests
{
    [Fact]
    public void ImportantMissingFile_TriggersFailFastCondition()
    {
        var nzbFile = new NzbFile { Subject = "\"Movie.mkv\" yEnc (1/1)" };
        var segment = new FetchFirstSegmentsStep.NzbFileWithFirstSegment
        {
            NzbFile = nzbFile,
            Header = null,
            First16KB = null,
            MissingFirstSegment = true,
            ReleaseDate = DateTimeOffset.UtcNow,
        };
        var fileInfos = GetFileInfosStep.GetFileInfos([segment], []);

        var missing = fileInfos
            .Where(x => segment.MissingFirstSegment && ReferenceEquals(x.NzbFile, nzbFile))
            .Where(x => DeadNzbFailFast.IsImportantFileName(x.FileName))
            .ToList();

        Assert.Single(missing);
        Assert.Equal("Movie.mkv", missing[0].FileName);
    }

    [Fact]
    public void JunkOnlyMissing_DoesNotTriggerFailFast()
    {
        var nzbFile = new NzbFile { Subject = "\"release.nfo\" yEnc (1/1)" };
        var segment = new FetchFirstSegmentsStep.NzbFileWithFirstSegment
        {
            NzbFile = nzbFile,
            Header = null,
            First16KB = null,
            MissingFirstSegment = true,
            ReleaseDate = DateTimeOffset.UtcNow,
        };
        var fileInfos = GetFileInfosStep.GetFileInfos([segment], []);

        var missing = fileInfos
            .Where(x => segment.MissingFirstSegment)
            .Where(x => DeadNzbFailFast.IsImportantFileName(x.FileName))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void ObfuscatedMissingName_IsTreatedAsImportant()
    {
        // Obfuscated names have no known extension — exclusion list must still fail-fast.
        var nzbFile = new NzbFile { Subject = "\"aB3xY9q\" yEnc (1/1)" };
        var segment = new FetchFirstSegmentsStep.NzbFileWithFirstSegment
        {
            NzbFile = nzbFile,
            Header = null,
            First16KB = null,
            MissingFirstSegment = true,
            ReleaseDate = DateTimeOffset.UtcNow,
        };
        var fileInfos = GetFileInfosStep.GetFileInfos([segment], []);

        var missing = fileInfos
            .Where(x => segment.MissingFirstSegment)
            .Where(x => DeadNzbFailFast.IsImportantFileName(x.FileName))
            .ToList();

        Assert.Single(missing);
    }

    [Fact]
    public void UsenetArticleNotFoundException_IsNonRetryable()
    {
        var ex = new UsenetArticleNotFoundException("<seg@example.com>");
        Assert.IsAssignableFrom<NonRetryableDownloadException>(ex);
        Assert.Equal("<seg@example.com>", ex.SegmentId);
    }

    [Theory]
    [InlineData("movie.rar", true)]
    [InlineData("aB3xY9q", true)]
    [InlineData("release.par2", false)]
    [InlineData("checksum.sfv", false)]
    [InlineData("info.nfo", false)]
    public void IsImportantFileName_MatchesExclusionList(string fileName, bool expectedImportant)
    {
        Assert.Equal(expectedImportant, DeadNzbFailFast.IsImportantFileName(fileName));
    }

    [Fact]
    public void FailMissingImportantFile_CachesSegmentAndThrowsNonRetryable()
    {
        var segmentId = $"cache-me-{Guid.NewGuid():N}@example.com";
        var nzbFile = new NzbFile
        {
            Subject = "\"dead.rar\" yEnc (1/1)",
            Segments = { new NzbSegment { MessageId = segmentId, Bytes = 100 } },
        };

        var ex = Assert.Throws<NonRetryableDownloadException>(() =>
            DeadNzbFailFast.FailMissingImportantFile(nzbFile));

        Assert.Contains("dead.rar", ex.Message);
        Assert.Throws<UsenetArticleNotFoundException>(() =>
            HealthCheckService.CheckCachedMissingSegmentIds([segmentId]));
    }
}
