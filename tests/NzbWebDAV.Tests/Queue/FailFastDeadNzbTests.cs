using NzbWebDAV.Exceptions;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Queue.DeobfuscationSteps._1.FetchFirstSegment;
using NzbWebDAV.Queue.DeobfuscationSteps._3.GetFileInfos;

namespace NzbWebDAV.Tests.Queue;

public class FailFastDeadNzbTests
{
    private static readonly HashSet<string> UnimportantExtensions =
        [".par2", ".nfo", ".txt", ".sfv", ".nzb", ".srr"];

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
            .Where(x => !UnimportantExtensions.Contains(Path.GetExtension(x.FileName).ToLowerInvariant()))
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
            .Where(x => !UnimportantExtensions.Contains(Path.GetExtension(x.FileName).ToLowerInvariant()))
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
            .Where(x => !UnimportantExtensions.Contains(Path.GetExtension(x.FileName).ToLowerInvariant()))
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
}
