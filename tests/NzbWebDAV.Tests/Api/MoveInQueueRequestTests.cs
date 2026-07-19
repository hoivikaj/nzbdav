using Microsoft.AspNetCore.Http;
using NzbWebDAV.Api.SabControllers.MoveInQueue;

namespace NzbWebDAV.Tests.Api;

public class MoveInQueueRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("top")]
    [InlineData("TOP")]
    public void IsMoveToTop_AcceptsTopPositions(string? position)
    {
        Assert.True(MoveInQueueRequest.IsMoveToTop(position));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("bottom")]
    [InlineData("-1")]
    public void IsMoveToTop_RejectsOtherPositions(string position)
    {
        Assert.Throws<BadHttpRequestException>(() => MoveInQueueRequest.IsMoveToTop(position));
    }

    [Fact]
    public async Task New_ParsesCommaSeparatedAndBodyIds()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?value={first},{second}&value2=0");
        context.Request.Body = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes($$"""{"nzo_ids":["{{third}}"]}"""));
        context.Request.ContentType = "application/json";

        var request = await MoveInQueueRequest.New(context);

        Assert.True(request.MoveToTop);
        Assert.Equal(3, request.NzoIds.Count);
        Assert.Contains(first, request.NzoIds);
        Assert.Contains(second, request.NzoIds);
        Assert.Contains(third, request.NzoIds);
    }
}
