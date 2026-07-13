using NzbWebDAV.Api.Controllers.GetWebdavItem;

namespace NzbWebDAV.Tests.Api;

public class GetWebdavItemRequestRangeTests
{
    [Theory]
    [InlineData("bytes=0-99", 0L, 99L, null)]
    [InlineData("bytes=100-", 100L, null, null)]
    [InlineData("bytes=-65536", null, null, 65536L)]
    public void TryParseRangeHeader_ParsesValidForms(
        string header, long? start, long? end, long? suffix)
    {
        Assert.True(GetWebdavItemRequest.TryParseRangeHeader(header, out var s, out var e, out var suf));
        Assert.Equal(start, s);
        Assert.Equal(end, e);
        Assert.Equal(suffix, suf);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bytes=abc-")]
    [InlineData("bytes=999999999999999999999-")]
    [InlineData("bytes=0-99,200-299")]
    [InlineData("bytes=0-xx")]
    [InlineData("items=0-10")]
    [InlineData("bytes=-")]
    public void TryParseRangeHeader_IgnoresMalformed(string header)
    {
        Assert.False(GetWebdavItemRequest.TryParseRangeHeader(header, out var s, out var e, out var suf));
        Assert.Null(s);
        Assert.Null(e);
        Assert.Null(suf);
    }
}
