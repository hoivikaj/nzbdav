using Microsoft.AspNetCore.Http;
using NzbWebDAV.Api.SabControllers.RemoveFromHistory;
using NzbWebDAV.Api.SabControllers.RemoveFromQueue;

namespace NzbWebDAV.Tests.Api;

public class SabDeleteRequestTests
{
    [Fact]
    public async Task QueueDelete_ParsesCommaSeparatedAndRepeatedIds()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var context = CreateContext(
            $"?value={first},{second}&value={third}&value=not-a-guid&del_files=1");

        var request = await RemoveFromQueueRequest.New(context);

        Assert.Equal([first, second, third], request.NzoIds);
        Assert.False(request.DeleteAll);
        Assert.True(request.DeleteFilesRequested);
    }

    [Fact]
    public async Task QueueDelete_AllOverridesExplicitIds()
    {
        var context = CreateContext($"?value={Guid.NewGuid()},all");

        var request = await RemoveFromQueueRequest.New(context);

        Assert.True(request.DeleteAll);
        Assert.Empty(request.NzoIds);
    }

    [Fact]
    public async Task HistoryDelete_ParsesFailedTokenAndAcceptsDelFiles()
    {
        var context = CreateContext("?value=failed&del_files=1");

        var request = await RemoveFromHistoryRequest.New(context);

        Assert.True(request.DeleteFailed);
        Assert.False(request.DeleteAll);
        Assert.True(request.DeleteFailedFilesRequested);
        Assert.False(request.DeleteCompletedFiles);
    }

    [Fact]
    public async Task HistoryDelete_AllTakesPrecedenceOverFailed()
    {
        var context = CreateContext("?value=failed,all&del_completed_files=1");

        var request = await RemoveFromHistoryRequest.New(context);

        Assert.True(request.DeleteAll);
        Assert.False(request.DeleteFailed);
        Assert.True(request.DeleteCompletedFiles);
    }

    private static DefaultHttpContext CreateContext(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        context.Request.Body = Stream.Null;
        return context;
    }
}
