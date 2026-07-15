using System.Diagnostics;
using System.Text;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class StreamingTimeoutTests
{
    [Fact]
    public async Task RunWithConnection_WithStreamingTimeout_FailsFastAndRetriesOnFreshConnection()
    {
        HangingNntpClient? hanging = null;
        var created = 0;
        using var pool = new ConnectionPool<INntpClient>(maxConnections: 2, _ =>
        {
            var n = Interlocked.Increment(ref created);
            if (n == 1)
            {
                hanging = new HangingNntpClient();
                return ValueTask.FromResult<INntpClient>(hanging);
            }

            return ValueTask.FromResult<INntpClient>(
                new HealthyNntpClient(new Dictionary<string, byte[]>
                {
                    ["seg"] = [1, 2, 3, 4],
                }));
        });

        using var client = new MultiConnectionNntpClient(
            pool,
            ProviderType.Pooled,
            new ProviderCircuitBreaker("streaming-timeout"),
            "streaming-timeout");

        using var cts = new CancellationTokenSource();
        using var timeoutScope = cts.Token.SetContext(new StreamingTimeoutContext
        {
            PerSegmentTimeout = TimeSpan.FromMilliseconds(200),
            MaxRetries = 1,
        });

        var outerCallbacks = 0;
        var sw = Stopwatch.StartNew();
        var response = await client.DecodedBodyAsync(
            "seg",
            _ => Interlocked.Increment(ref outerCallbacks),
            cts.Token);
        sw.Stop();

        Assert.True(response.Success);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Expected fast failover, took {sw.Elapsed}");
        Assert.NotNull(hanging);
        Assert.Equal(1, hanging!.BodyRequestCount);
        Assert.Equal(1, hanging.CallbackCount);
        Assert.True(hanging.Disposed);
        Assert.Equal(1, outerCallbacks);
        Assert.Equal(2, created);
    }

    [Fact]
    public async Task RunWithConnection_WithoutStreamingTimeout_DoesNotCancelAfter()
    {
        var hanging = new HangingNntpClient();
        using var pool = new ConnectionPool<INntpClient>(
            maxConnections: 1, _ => ValueTask.FromResult<INntpClient>(hanging));

        using var client = new MultiConnectionNntpClient(
            pool,
            ProviderType.Pooled,
            new ProviderCircuitBreaker("no-streaming-timeout"),
            "no-streaming-timeout");

        using var cts = new CancellationTokenSource();
        var bodyTask = client.DecodedBodyAsync("seg", onConnectionReadyAgain: null, cts.Token);

        // WaitAsync abandons the await without cancelling the caller's token.
        // If CancelAfter had been applied, the hang would observe cancellation.
        await Assert.ThrowsAsync<TimeoutException>(() =>
            bodyTask.WaitAsync(TimeSpan.FromMilliseconds(300)));

        Assert.Equal(1, hanging.BodyRequestCount);
        Assert.False(hanging.SawCancellation);
        Assert.Equal(0, hanging.CallbackCount);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bodyTask);
        Assert.True(hanging.SawCancellation);
        Assert.Equal(1, hanging.CallbackCount);
    }

    [Fact]
    public async Task RunWithConnection_StreamingTimeoutExhausted_ThrowsTimeoutException()
    {
        var created = 0;
        using var pool = new ConnectionPool<INntpClient>(maxConnections: 2, _ =>
        {
            Interlocked.Increment(ref created);
            return ValueTask.FromResult<INntpClient>(new HangingNntpClient());
        });

        using var client = new MultiConnectionNntpClient(
            pool,
            ProviderType.Pooled,
            new ProviderCircuitBreaker("streaming-timeout-exhausted"),
            "streaming-timeout-exhausted");

        using var cts = new CancellationTokenSource();
        using var timeoutScope = cts.Token.SetContext(new StreamingTimeoutContext
        {
            PerSegmentTimeout = TimeSpan.FromMilliseconds(100),
            MaxRetries = 1,
        });

        var outerCallbacks = 0;
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            client.DecodedBodyAsync("seg", _ => Interlocked.Increment(ref outerCallbacks), cts.Token));
        sw.Stop();

        Assert.Contains("2 attempts", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(1, outerCallbacks);
        Assert.Equal(2, created);
    }

    [Fact]
    public async Task RunWithConnection_StreamingTimeoutExhausted_RecordsBreakerFailure()
    {
        var breaker = new ProviderCircuitBreaker("streaming-timeout-breaker");
        using var pool = new ConnectionPool<INntpClient>(
            maxConnections: 2,
            _ => ValueTask.FromResult<INntpClient>(new HangingNntpClient()));

        using var client = new MultiConnectionNntpClient(
            pool, ProviderType.Pooled, breaker, "streaming-timeout-breaker");

        using var cts = new CancellationTokenSource();
        using var timeoutScope = cts.Token.SetContext(new StreamingTimeoutContext
        {
            PerSegmentTimeout = TimeSpan.FromMilliseconds(50),
            MaxRetries = 1,
        });

        // Three exhausted segments → consecutive-failure trip threshold (3).
        for (var i = 0; i < 3; i++)
        {
            Assert.False(breaker.IsTripped);
            await Assert.ThrowsAsync<TimeoutException>(() =>
                client.DecodedBodyAsync($"seg-{i}", onConnectionReadyAgain: null, cts.Token));
        }

        Assert.True(breaker.IsTripped);
        Assert.True(breaker.TrippedUntilMs > 0);
    }

    [Fact]
    public async Task RunWithConnection_StreamingTimeoutThenSuccess_DoesNotTripBreaker()
    {
        var breaker = new ProviderCircuitBreaker("streaming-timeout-recover");
        var created = 0;
        using var pool = new ConnectionPool<INntpClient>(maxConnections: 2, _ =>
        {
            var n = Interlocked.Increment(ref created);
            if (n == 1)
                return ValueTask.FromResult<INntpClient>(new HangingNntpClient());
            return ValueTask.FromResult<INntpClient>(
                new HealthyNntpClient(new Dictionary<string, byte[]> { ["seg"] = [1, 2, 3] }));
        });

        using var client = new MultiConnectionNntpClient(
            pool, ProviderType.Pooled, breaker, "streaming-timeout-recover");

        using var cts = new CancellationTokenSource();
        using var timeoutScope = cts.Token.SetContext(new StreamingTimeoutContext
        {
            PerSegmentTimeout = TimeSpan.FromMilliseconds(50),
            MaxRetries = 1,
        });

        // Timeout then success on retry — exhaustion path never runs, so no
        // breaker failure is recorded for this segment.
        var response = await client.DecodedBodyAsync("seg", onConnectionReadyAgain: null, cts.Token);
        Assert.True(response.Success);
        Assert.False(breaker.IsTripped);
        Assert.Equal(0, breaker.TrippedUntilMs);
    }

    /// <summary>
    /// BODY that hangs until cancelled, firing NotRetrieved exactly once
    /// (in-flight cancel → connection not reusable).
    /// </summary>
    private sealed class HangingNntpClient : NntpClient
    {
        private int _callbackCount;

        public int BodyRequestCount { get; private set; }
        public int CallbackCount => Volatile.Read(ref _callbackCount);
        public bool SawCancellation { get; private set; }
        public bool Disposed { get; private set; }

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            DecodedBodyAsync(segmentId, null, cancellationToken);

        public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken)
        {
            BodyRequestCount++;
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("Hang was expected to be cancelled.");
            }
            catch (OperationCanceledException)
            {
                SawCancellation = true;
                // Mid-command cancel leaves the socket unclean → NotRetrieved (replace).
                Interlocked.Increment(ref _callbackCount);
                onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved);
                throw;
            }
        }

        public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
            IReadOnlyList<SegmentId> segmentIds,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task ConnectAsync(
            string host, int port, bool useSsl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<UsenetResponse> AuthenticateAsync(
            string user, string pass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetStatResponse> StatAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetHeadResponse> HeadAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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

        public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
            string segmentId, CancellationToken cancellationToken) =>
            Task.FromResult(new UsenetExclusiveConnection(null));

        public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
            IReadOnlyList<SegmentId> segmentIds, CancellationToken cancellationToken) =>
            Task.FromResult(new UsenetExclusiveConnection(null));

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId,
            UsenetExclusiveConnection exclusiveConnection,
            CancellationToken cancellationToken) =>
            DecodedBodyAsync(segmentId, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);

        public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
            IReadOnlyList<SegmentId> segmentIds,
            UsenetExclusiveConnection exclusiveConnection,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId,
            UsenetExclusiveConnection exclusiveConnection,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class HealthyNntpClient(IReadOnlyDictionary<string, byte[]> segments) : NntpClient
    {
        public override Task ConnectAsync(
            string host, int port, bool useSsl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<UsenetResponse> AuthenticateAsync(
            string user, string pass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetStatResponse> StatAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetHeadResponse> HeadAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            DecodedBodyAsync(segmentId, null, cancellationToken);

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = segmentId.ToString();
            if (!segments.TryGetValue(key, out var bytes))
                throw new InvalidOperationException($"Missing segment {key}");

            onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
            return Task.FromResult(new UsenetDecodedBodyResponse
            {
                SegmentId = key,
                ResponseCode = (int)UsenetResponseType.ArticleRetrievedBodyFollows,
                ResponseMessage = "222 ok",
                Stream = new YencStream(new MemoryStream(EncodeYenc(bytes), writable: false)),
            });
        }

        public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
            IReadOnlyList<SegmentId> segmentIds,
            Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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

        public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
            string segmentId, CancellationToken cancellationToken) =>
            Task.FromResult(new UsenetExclusiveConnection(null));

        public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
            IReadOnlyList<SegmentId> segmentIds, CancellationToken cancellationToken) =>
            Task.FromResult(new UsenetExclusiveConnection(null));

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId,
            UsenetExclusiveConnection exclusiveConnection,
            CancellationToken cancellationToken) =>
            DecodedBodyAsync(segmentId, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);

        public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
            IReadOnlyList<SegmentId> segmentIds,
            UsenetExclusiveConnection exclusiveConnection,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId,
            UsenetExclusiveConnection exclusiveConnection,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override void Dispose()
        {
        }

        private static byte[] EncodeYenc(ReadOnlySpan<byte> source)
        {
            using var output = new MemoryStream(source.Length + 128);
            output.Write(Encoding.ASCII.GetBytes(
                $"=ybegin line=128 size={source.Length} name=fake.bin\r\n"));
            foreach (var value in source)
                output.WriteByte(unchecked((byte)(value + 42)));
            output.Write(Encoding.ASCII.GetBytes("\r\n"));
            output.Write(Encoding.ASCII.GetBytes($"=yend size={source.Length}\r\n"));
            return output.ToArray();
        }
    }
}
