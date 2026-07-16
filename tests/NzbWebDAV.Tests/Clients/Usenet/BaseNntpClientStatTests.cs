using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;
using UsenetSharp.Clients;
using UsenetSharp.Models;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class BaseNntpClientStatTests
{
    [Fact]
    public async Task StatAsync_With223_ReturnsExistsResponse()
    {
        using var client = new BaseNntpClient(new ScriptedUsenetClient(223));

        var response = await client.StatAsync("seg@example", CancellationToken.None);

        Assert.Equal(UsenetResponseType.ArticleExists, response.ResponseType);
        Assert.True(response.ArticleExists);
    }

    [Fact]
    public async Task StatAsync_With430_ReturnsDefinitiveMissing()
    {
        using var client = new BaseNntpClient(new ScriptedUsenetClient(430));

        var response = await client.StatAsync("seg@example", CancellationToken.None);

        Assert.True(UsenetArticleAvailability.IsDefinitiveMissing(response));
        Assert.False(response.ArticleExists);
    }

    [Fact]
    public async Task StatAsync_With451_ReturnsDefinitiveMissing()
    {
        using var client = new BaseNntpClient(new ScriptedUsenetClient(451));

        var response = await client.StatAsync("seg@example", CancellationToken.None);

        Assert.True(UsenetArticleAvailability.IsDefinitiveMissing(response));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(480)]
    [InlineData(503)]
    public async Task StatAsync_WithConnectionLevelCode_ThrowsUnexpectedResponse(int responseCode)
    {
        using var client = new BaseNntpClient(new ScriptedUsenetClient(responseCode));

        var exception = await Assert.ThrowsAsync<UsenetUnexpectedResponseException>(() =>
            client.StatAsync("seg@example", CancellationToken.None));

        Assert.IsAssignableFrom<RetryableDownloadException>(exception);
        Assert.Equal("seg@example", exception.SegmentId);
    }

    private sealed class ScriptedUsenetClient(int responseCode) : IUsenetClient
    {
        public bool IsConnected => true;
        public bool IsHealthy => true;

        public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<UsenetResponse> AuthenticateAsync(
            string user, string pass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken) =>
            Task.FromResult(new UsenetStatResponse
            {
                ResponseCode = responseCode,
                ResponseMessage = $"{responseCode} <{segmentId}>",
                ArticleExists = responseCode == (int)UsenetResponseType.ArticleExists,
            });

        public Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsenetBodyResponse> BodyAsync(SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsenetBodyResponse> BodyAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsenetArticleResponse> ArticleAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsenetArticleResponse> ArticleAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WaitForReadyAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
