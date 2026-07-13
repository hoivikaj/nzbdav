using System.IO;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Models;
using NzbWebDAV.Services.Metrics;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class MultiProviderNntpClientTests
{
    [Fact]
    public async Task BatchResponse_WithUnexpectedResponse_RetriesOnSameProvider()
    {
        // A stale pooled connection surfaces the server's buffered goodbye line
        // (e.g. "400 idle timeout") as the batch response. The segment must be
        // retried on the same provider instead of being reported missing.
        var connection = new ScriptedNntpClient
        {
            BatchResponseCode = 400,
            SingularResponseCode = 222,
        };
        using var client = new MultiProviderNntpClient([CreateProvider(connection)]);

        var batch = await client.DecodedBodiesAsync(
            ["segment"], onConnectionReadyAgain: null, CancellationToken.None);
        var response = await batch.Responses[0];

        Assert.Equal(UsenetResponseType.ArticleRetrievedBodyFollows, response.ResponseType);
        Assert.Equal(1, connection.SingularRequests);
    }

    [Fact]
    public async Task BatchResponse_WithCleanNotFound_RetriesOnSameProvider()
    {
        var connection = new ScriptedNntpClient
        {
            BatchResponseCode = 430,
            SingularResponseCode = 222,
        };
        using var client = new MultiProviderNntpClient([CreateProvider(connection)]);

        var batch = await client.DecodedBodiesAsync(
            ["segment"], onConnectionReadyAgain: null, CancellationToken.None);
        var response = await batch.Responses[0];

        Assert.Equal(UsenetResponseType.ArticleRetrievedBodyFollows, response.ResponseType);
        Assert.Equal(1, connection.SingularRequests);
    }

    [Fact]
    public async Task BatchResponse_WithUnexpectedResponse_ThrowsRetryableWhenRetriesFail()
    {
        var connection = new ScriptedNntpClient
        {
            BatchResponseCode = 400,
            SingularException = segmentId =>
                new UsenetUnexpectedResponseException(segmentId, "400 idle timeout"),
        };
        using var client = new MultiProviderNntpClient([CreateProvider(connection)]);

        var batch = await client.DecodedBodiesAsync(
            ["segment"], onConnectionReadyAgain: null, CancellationToken.None);

        // A connection-level failure must surface as retryable,
        // never as a (permanent) missing article.
        var exception = await Assert.ThrowsAsync<UsenetUnexpectedResponseException>(
            () => batch.Responses[0]);
        Assert.IsAssignableFrom<RetryableDownloadException>(exception);
    }

    [Fact]
    public async Task BatchSetup_WithStaleCancellation_RetriesOnAnotherConnection()
    {
        var connection = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            BatchException = requestNumber => requestNumber == 1
                ? new TaskCanceledException("Cancellation recorded by an earlier request.")
                : null,
        };
        using var client = new MultiProviderNntpClient([CreateProvider(connection)]);

        var batch = await client.DecodedBodiesAsync(
            ["segment"], onConnectionReadyAgain: null, CancellationToken.None);
        var response = await batch.Responses[0];

        Assert.Equal(UsenetResponseType.ArticleRetrievedBodyFollows, response.ResponseType);
        Assert.Equal(2, connection.BatchRequests);
    }

    [Fact]
    public async Task BatchSetup_WithCurrentRequestCancellation_DoesNotRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var connection = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            BatchException = _ =>
            {
                cancellation.Cancel();
                return new TaskCanceledException("Current request was cancelled.");
            },
        };
        using var client = new MultiProviderNntpClient([CreateProvider(connection)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.DecodedBodiesAsync(
                ["segment"], onConnectionReadyAgain: null, cancellation.Token));
        Assert.Equal(1, connection.BatchRequests);
    }

    [Theory]
    [InlineData(222)]
    [InlineData(430)]
    public async Task PipelinedBodyResponse_RecordsFetchMetric(int responseCode)
    {
        var writer = new MetricsWriter();
        var connection = new ScriptedNntpClient
        {
            BatchResponseCode = responseCode,
            SingularResponseCode = responseCode,
        };
        using var client = new MultiProviderNntpClient(
            [CreateProvider(connection)], metricsWriter: writer);

        await foreach (var result in client.DecodedBodiesPipelinedAsync(
                           ["segment"], 1, CancellationToken.None))
            if (result.Stream != null)
                await result.Stream.DisposeAsync();

        Assert.Equal(1, writer.Stats.QueuedFetches);
        Assert.Equal(1, connection.BatchRequests);
        Assert.Equal(0, connection.SingularRequests);
    }

    [Fact]
    public async Task StorageGroup_SameGroupMiss_SkipsSiblingProvider()
    {
        var first = new ScriptedNntpClient
        {
            BatchResponseCode = 430,
            SingularResponseCode = 430,
        };
        var sibling = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            SingularResponseCode = 222,
        };
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(first, host: "a.example", storageGroup: "omicron"),
            CreateProvider(sibling, host: "b.example", storageGroup: "omicron"),
        ]);

        var response = await client.DecodedBodyAsync("segment", CancellationToken.None);

        Assert.Equal(UsenetResponseType.NoArticleWithThatMessageId, response.ResponseType);
        Assert.Equal(1, first.SingularRequests);
        Assert.Equal(0, sibling.SingularRequests);
    }

    [Fact]
    public async Task StorageGroup_ConnectionError_DoesNotSkipSibling()
    {
        var first = new ScriptedNntpClient
        {
            BatchResponseCode = 430,
            SingularException = _ => new IOException("connection reset"),
        };
        var sibling = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            SingularResponseCode = 222,
        };
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(first, host: "a.example", storageGroup: "omicron"),
            CreateProvider(sibling, host: "b.example", storageGroup: "omicron"),
        ]);

        var response = await client.DecodedBodyAsync("segment", CancellationToken.None);

        Assert.Equal(UsenetResponseType.ArticleRetrievedBodyFollows, response.ResponseType);
        // MultiConnectionNntpClient retries the failed connection once before failing over.
        Assert.True(first.SingularRequests >= 1);
        Assert.Equal(1, sibling.SingularRequests);
    }

    [Fact]
    public async Task StorageGroup_DifferentGroups_StillFailsOver()
    {
        var first = new ScriptedNntpClient
        {
            BatchResponseCode = 430,
            SingularResponseCode = 430,
        };
        var other = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            SingularResponseCode = 222,
        };
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(first, host: "a.example", storageGroup: "omicron"),
            CreateProvider(other, host: "b.example", storageGroup: "eweka"),
        ]);

        var response = await client.DecodedBodyAsync("segment", CancellationToken.None);

        Assert.Equal(UsenetResponseType.ArticleRetrievedBodyFollows, response.ResponseType);
        Assert.Equal(1, first.SingularRequests);
        Assert.Equal(1, other.SingularRequests);
    }

    [Fact]
    public async Task StorageGroup_Empty_PreservesFailover()
    {
        var first = new ScriptedNntpClient
        {
            BatchResponseCode = 430,
            SingularResponseCode = 430,
        };
        var second = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            SingularResponseCode = 222,
        };
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(first, host: "a.example"),
            CreateProvider(second, host: "b.example"),
        ]);

        var response = await client.DecodedBodyAsync("segment", CancellationToken.None);

        Assert.Equal(UsenetResponseType.ArticleRetrievedBodyFollows, response.ResponseType);
        Assert.Equal(1, first.SingularRequests);
        Assert.Equal(1, second.SingularRequests);
    }

    [Fact]
    public async Task StorageGroup_BatchPrimaryRetry_NotSkippedBySameGroupSibling()
    {
        var primary = new ScriptedNntpClient
        {
            BatchResponseCode = 430,
            SingularResponseCode = 222,
        };
        var sibling = new ScriptedNntpClient
        {
            BatchResponseCode = 430,
            SingularResponseCode = 430,
        };
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(primary, host: "a.example", storageGroup: "omicron"),
            CreateProvider(sibling, host: "b.example", storageGroup: "omicron"),
        ]);

        var batch = await client.DecodedBodiesAsync(
            ["segment"], onConnectionReadyAgain: null, CancellationToken.None);
        var response = await batch.Responses[0];

        Assert.Equal(UsenetResponseType.ArticleRetrievedBodyFollows, response.ResponseType);
        Assert.Equal(1, primary.SingularRequests);
        Assert.Equal(0, sibling.SingularRequests);
    }

    [Fact]
    public async Task StorageGroup_StreamingTerminalMiss_FiresCompletionCallbackOnce()
    {
        var first = new ScriptedNntpClient
        {
            BatchResponseCode = 430,
            SingularResponseCode = 430,
        };
        var sibling = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            SingularResponseCode = 222,
        };
        var callbacks = new List<ArticleBodyResult>();
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(first, host: "a.example", storageGroup: "omicron"),
            CreateProvider(sibling, host: "b.example", storageGroup: "omicron"),
        ]);

        var response = await client.DecodedBodyAsync(
            "segment", callbacks.Add, CancellationToken.None);

        Assert.Equal(UsenetResponseType.NoArticleWithThatMessageId, response.ResponseType);
        Assert.Equal(1, first.SingularRequests);
        Assert.Equal(0, sibling.SingularRequests);
        Assert.Single(callbacks);
        Assert.Equal(ArticleBodyResult.NotRetrieved, callbacks[0]);
    }

    [Fact]
    public async Task StorageGroup_SameGroupMiss451_SkipsSiblingProvider()
    {
        var first = new ScriptedNntpClient
        {
            BatchResponseCode = 451,
            SingularResponseCode = 451,
        };
        var sibling = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            SingularResponseCode = 222,
        };
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(first, host: "a.example", storageGroup: "omicron"),
            CreateProvider(sibling, host: "b.example", storageGroup: "omicron"),
        ]);

        var response = await client.DecodedBodyAsync("segment", CancellationToken.None);

        Assert.Equal(451, response.ResponseCode);
        Assert.Equal(1, first.SingularRequests);
        Assert.Equal(0, sibling.SingularRequests);
    }

    [Fact]
    public async Task StorageGroup_DifferentGroups_FailsOverOn451()
    {
        var first = new ScriptedNntpClient
        {
            BatchResponseCode = 451,
            SingularResponseCode = 451,
        };
        var other = new ScriptedNntpClient
        {
            BatchResponseCode = 222,
            SingularResponseCode = 222,
        };
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(first, host: "a.example", storageGroup: "omicron"),
            CreateProvider(other, host: "b.example", storageGroup: "eweka"),
        ]);

        var response = await client.DecodedBodyAsync("segment", CancellationToken.None);

        Assert.Equal(UsenetResponseType.ArticleRetrievedBodyFollows, response.ResponseType);
        Assert.Equal(1, first.SingularRequests);
        Assert.Equal(1, other.SingularRequests);
    }

    [Fact]
    public async Task CheckAllSegmentsAsync_With451AcrossProviders_ThrowsArticleNotFound()
    {
        var first = new ScriptedNntpClient
        {
            BatchResponseCode = 451,
            SingularResponseCode = 451,
        };
        var second = new ScriptedNntpClient
        {
            BatchResponseCode = 451,
            SingularResponseCode = 451,
        };
        using var client = new MultiProviderNntpClient(
        [
            CreateProvider(first, host: "a.example"),
            CreateProvider(second, host: "b.example"),
        ]);

        await Assert.ThrowsAsync<UsenetArticleNotFoundException>(() =>
            client.CheckAllSegmentsAsync(["segment"], 1, null, CancellationToken.None));

        Assert.Equal(1, first.SingularRequests);
        Assert.Equal(1, second.SingularRequests);
    }

    [Fact]
    public async Task CheckAllSegmentsAsync_With400_ThrowsUnexpectedResponse()
    {
        var connection = new ScriptedNntpClient
        {
            BatchResponseCode = 400,
            SingularResponseCode = 400,
        };
        using var client = new MultiProviderNntpClient([CreateProvider(connection)]);

        var exception = await Assert.ThrowsAsync<UsenetUnexpectedResponseException>(() =>
            client.CheckAllSegmentsAsync(["segment"], 1, null, CancellationToken.None));

        Assert.IsAssignableFrom<RetryableDownloadException>(exception);
    }

    [Fact]
    public async Task BatchResponse_With451Exhausted_ThrowsArticleNotFound()
    {
        var connection = new ScriptedNntpClient
        {
            BatchResponseCode = 451,
            SingularResponseCode = 451,
        };
        using var client = new MultiProviderNntpClient([CreateProvider(connection)]);

        var batch = await client.DecodedBodiesAsync(
            ["segment"], onConnectionReadyAgain: null, CancellationToken.None);

        await Assert.ThrowsAsync<UsenetArticleNotFoundException>(() => batch.Responses[0]);
    }

    private static MultiConnectionNntpClient CreateProvider(
        INntpClient connection,
        string host = "test",
        string storageGroup = "")
    {
        var pool = new ConnectionPool<INntpClient>(
            maxConnections: 1, _ => ValueTask.FromResult(connection));
        return new MultiConnectionNntpClient(
            pool,
            ProviderType.Pooled,
            new ProviderCircuitBreaker(host),
            host,
            storageGroup: storageGroup);
    }

    private sealed class ScriptedNntpClient : NntpClient
    {
        public required int BatchResponseCode { get; init; }
        public int SingularResponseCode { get; init; } = 222;
        public Func<int, Exception?>? BatchException { get; init; }
        public Func<string, Exception>? SingularException { get; init; }
        public int BatchRequests { get; private set; }
        public int SingularRequests { get; private set; }

        public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
            IReadOnlyList<SegmentId> segmentIds,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken)
        {
            BatchRequests++;
            var exception = BatchException?.Invoke(BatchRequests);
            if (exception != null)
                throw exception;

            var responses = segmentIds
                .Select(segmentId => Task.FromResult(CreateResponse(segmentId, BatchResponseCode)))
                .ToArray();
            onConnectionReadyAgain?.Invoke(ToArticleBodyResult(BatchResponseCode));
            return Task.FromResult(new UsenetDecodedBodyBatch { Responses = responses });
        }

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken)
        {
            SingularRequests++;
            if (SingularException != null)
                throw SingularException(segmentId.ToString());

            var response = CreateResponse(segmentId, SingularResponseCode);
            onConnectionReadyAgain?.Invoke(ToArticleBodyResult(SingularResponseCode));
            return Task.FromResult(response);
        }

        private static ArticleBodyResult ToArticleBodyResult(int responseCode) => responseCode switch
        {
            (int)UsenetResponseType.ArticleRetrievedBodyFollows => ArticleBodyResult.Retrieved,
            (int)UsenetResponseType.NoArticleWithThatMessageId => ArticleBodyResult.NotFound,
            UsenetArticleAvailability.ArticleUnavailable => ArticleBodyResult.NotFound,
            _ => ArticleBodyResult.NotRetrieved,
        };

        private static UsenetDecodedBodyResponse CreateResponse(SegmentId segmentId, int responseCode)
        {
            var success = responseCode == (int)UsenetResponseType.ArticleRetrievedBodyFollows;
            return new UsenetDecodedBodyResponse
            {
                SegmentId = segmentId.ToString(),
                ResponseCode = responseCode,
                ResponseMessage = $"{responseCode} scripted response",
                Stream = success ? new YencStream(new MemoryStream([], writable: false)) : null,
            };
        }

        public override Task ConnectAsync(
            string host, int port, bool useSsl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<UsenetResponse> AuthenticateAsync(
            string user, string pass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetStatResponse> StatAsync(
            SegmentId segmentId, CancellationToken cancellationToken)
        {
            SingularRequests++;
            if (SingularException != null)
                throw SingularException(segmentId.ToString());

            return Task.FromResult(new UsenetStatResponse
            {
                ResponseCode = SingularResponseCode,
                ResponseMessage = $"{SingularResponseCode} scripted stat <{segmentId}>",
                ArticleExists = SingularResponseCode == (int)UsenetResponseType.ArticleExists,
            });
        }

        public override Task<UsenetHeadResponse> HeadAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            DecodedBodyAsync(segmentId, null, cancellationToken);

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override void Dispose()
        {
        }
    }
}
