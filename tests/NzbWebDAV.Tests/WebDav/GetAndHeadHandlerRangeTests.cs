using NzbWebDAV.Database.Models;
using NzbWebDAV.Services;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.Tests.WebDav;

public class GetAndHeadHandlerRangeTests
{
    [Theory]
    [InlineData("npt=0.000-")]
    [InlineData("bytes=99999999999999999999-")]
    [InlineData("bytes=-")]
    [InlineData("bytes=0-1,5-9")]
    [InlineData("items=0-9")]
    [InlineData("")]
    public void TryResolveRange_IgnoresMalformedOrMultiRange(string header)
    {
        Assert.Null(GetAndHeadHandlerPatch.TryResolveRange(isHeadRequest: false, header));
    }

    [Fact]
    public void TryResolveRange_ParsesByteRange()
    {
        var range = GetAndHeadHandlerPatch.TryResolveRange(isHeadRequest: false, "bytes=0-499");
        Assert.NotNull(range);
        Assert.Equal(0L, range!.Start);
        Assert.Equal(499L, range.End);
    }

    [Fact]
    public void TryResolveRange_ParsesSuffixRange()
    {
        var range = GetAndHeadHandlerPatch.TryResolveRange(isHeadRequest: false, "bytes=-500");
        Assert.NotNull(range);
        Assert.Null(range!.Start);
        Assert.Equal(500L, range.End);
    }

    [Theory]
    [InlineData("bytes=0-0")]
    [InlineData("bytes=-500")]
    [InlineData("npt=0.000-")]
    public void TryResolveRange_IgnoresRangeOnHead(string header)
    {
        Assert.Null(GetAndHeadHandlerPatch.TryResolveRange(isHeadRequest: true, header));
    }

    [Fact]
    public void CompletedFullRead_ClearsStreamingFailures()
    {
        var tracker = new StreamingFailureTracker();
        var item = NewDavItem();
        tracker.RecordFailure(item.Id);

        var cleared = GetAndHeadHandlerPatch.ClearStreamingFailureAfterCompletedRead(
            tracker, item, isHeadRequest: false, copySucceeded: true, copyStart: 0, copyEnd: null, streamLength: 100);

        Assert.True(cleared);
        Assert.Equal(0, tracker.GetFailureCount(item.Id));
    }

    [Fact]
    public void CompletedExplicitFullRange_ClearsStreamingFailures()
    {
        var tracker = new StreamingFailureTracker();
        var item = NewDavItem();
        tracker.RecordFailure(item.Id);

        var cleared = GetAndHeadHandlerPatch.ClearStreamingFailureAfterCompletedRead(
            tracker, item, isHeadRequest: false, copySucceeded: true, copyStart: 0, copyEnd: 99, streamLength: 100);

        Assert.True(cleared);
        Assert.Equal(0, tracker.GetFailureCount(item.Id));
    }

    [Theory]
    [InlineData(true, false, 0, 99, 100)]
    [InlineData(false, false, 1, 99, 100)]
    [InlineData(false, false, 0, 98, 100)]
    [InlineData(false, true, 0, 99, 100)]
    public void IncompleteOrUnsuccessfulRead_DoesNotClearStreamingFailures(
        bool isHeadRequest,
        bool copyFailed,
        long copyStart,
        long copyEnd,
        long streamLength)
    {
        var tracker = new StreamingFailureTracker();
        var item = NewDavItem();
        tracker.RecordFailure(item.Id);

        var cleared = GetAndHeadHandlerPatch.ClearStreamingFailureAfterCompletedRead(
            tracker, item, isHeadRequest, !copyFailed, copyStart, copyEnd, streamLength);

        Assert.False(cleared);
        Assert.Equal(1, tracker.GetFailureCount(item.Id));
    }

    private static DavItem NewDavItem()
    {
        return new DavItem { Id = Guid.NewGuid() };
    }
}
