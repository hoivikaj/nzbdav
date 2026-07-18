using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;
using SharpCompress.Common;
using SharpCompress.Common.Rar;

namespace NzbWebDAV.Tests.Utils;

public class RarUtilTests
{
    [Fact]
    public void TryMapHeaderParseFailure_WrapsSeekPastEndAsCorruptRarException()
    {
        using var stream = new MemoryStream(new byte[100]);
        var seekPastEnd = new ArgumentOutOfRangeException(
            "offset",
            52223980L,
            "Seek position is outside stream bounds.");

        Assert.True(RarUtil.TryMapHeaderParseFailure(seekPastEnd, stream, out var mapped));
        var ex = Assert.IsType<RarSeekPastEndException>(mapped);
        Assert.IsAssignableFrom<CorruptRarException>(mapped);
        Assert.Contains("seek past stream end", ex.Message);
        Assert.Contains("52223980", ex.Message);
        Assert.Contains("stream length 100", ex.Message);
        Assert.True(ex.IsNonRetryableDownloadException());
    }

    [Fact]
    public void TryMapHeaderParseFailure_MapsTruncatedRarHeaderSeekPastEnd()
    {
        using var stream = new MemoryStream(new byte[50]);
        var truncated = new RarHeaderReadException(
            "Failed to skip RAR packed data: seek past stream end at offset 100",
            truncated: true);

        Assert.True(RarUtil.TryMapHeaderParseFailure(truncated, stream, out var mapped));
        var ex = Assert.IsType<RarSeekPastEndException>(mapped);
        Assert.Contains("seek past stream end", ex.Message);
        Assert.Contains("stream length 50", ex.Message);
        Assert.True(ex.IsNonRetryableDownloadException());
    }

    [Fact]
    public void TryMapHeaderParseFailure_MapsIncompleteArchiveAsCorruptRar()
    {
        using var stream = new MemoryStream(new byte[10]);
        var incomplete = new IncompleteArchiveException("unexpected EOF");

        Assert.True(RarUtil.TryMapHeaderParseFailure(incomplete, stream, out var mapped));
        var ex = Assert.IsType<CorruptRarException>(mapped);
        Assert.Contains("unexpected end of stream", ex.Message);
        Assert.True(ex.IsNonRetryableDownloadException());
    }

    [Fact]
    public async Task FindFirstFileHeaderAsync_WrapsInvalidFormatAsCorruptRarException()
    {
        await using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        var ex = await Assert.ThrowsAsync<CorruptRarException>(async () =>
            await RarUtil.FindFirstFileHeaderAsync(
                stream,
                password: null,
                _ => true,
                CancellationToken.None));

        Assert.StartsWith("Failed to parse RAR volume headers:", ex.Message);
        Assert.True(ex.IsNonRetryableDownloadException());
    }

    [Fact]
    public void KnownDownloadClassification_TreatsCorruptRarAndInvalidFormatAsNonRetryable()
    {
        var corrupt = new CorruptRarException(
            "Failed to parse RAR volume headers (seek past stream end at offset 1; stream length 0)");
        Assert.True(corrupt.IsNonRetryableDownloadException());

        var wrappedInvalidFormat = new Exception(
            "wrapper",
            new InvalidFormatException("bad rar"));
        Assert.True(
            wrappedInvalidFormat.TryGetCausingException<InvalidFormatException>(out _));
        Assert.True(IsKnownDownloadStyle(wrappedInvalidFormat, out var reason));
        Assert.Equal("bad rar", reason);

        Assert.True(new IncompleteArchiveException("eof").IsNonRetryableDownloadException());
        Assert.True(new RarHeaderReadException("bad crc", truncated: false)
            .IsNonRetryableDownloadException());
    }

    // Mirrors ExceptionMiddleware.IsKnownDownloadException chain walk.
    private static bool IsKnownDownloadStyle(Exception e, out string message)
    {
        for (var current = e; current != null; current = current.InnerException)
        {
            if (current.IsRetryableDownloadException() || current.IsNonRetryableDownloadException())
            {
                message = current.Message;
                return true;
            }
        }

        message = string.Empty;
        return false;
    }
}
