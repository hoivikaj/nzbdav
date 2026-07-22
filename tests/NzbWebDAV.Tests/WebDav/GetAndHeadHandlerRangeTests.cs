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
}
