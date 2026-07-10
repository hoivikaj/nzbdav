using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Models;
using NzbWebDAV.Streams;
using UsenetSharp.Models;
using UsenetSharp.Streams;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

[MemoryDiagnoser]
public class YencDecodeBenchmarks
{
    private byte[] _decoded = null!;
    private byte[] _encoded = null!;

    [Params(256 * 1024, 1024 * 1024)]
    public int SegmentSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _decoded = new byte[SegmentSize];
        new Random(42).NextBytes(_decoded);
        _encoded = EncodeYenc(_decoded);
    }

    [Benchmark(Baseline = true)]
    public async Task DecodeYencSegment()
    {
        await using var source = new MemoryStream(_encoded, writable: false);
        await using var stream = new YencStream(source);
        await stream.CopyToAsync(Stream.Null);
    }

    [Benchmark]
    public async Task CopyDecodedSegment()
    {
        await using var stream = new MemoryStream(_decoded, writable: false);
        await stream.CopyToAsync(Stream.Null);
    }

    internal static byte[] EncodeYenc(ReadOnlySpan<byte> source)
    {
        using var output = new MemoryStream(source.Length + source.Length / 100);
        WriteAscii(output, $"=ybegin line=128 size={source.Length} name=benchmark.bin\r\n");

        var lineLength = 0;
        foreach (var value in source)
        {
            var encoded = unchecked((byte)(value + 42));
            if (encoded is 0 or (byte)'\n' or (byte)'\r' or (byte)'=')
            {
                output.WriteByte((byte)'=');
                output.WriteByte(unchecked((byte)(encoded + 64)));
                lineLength += 2;
            }
            else
            {
                output.WriteByte(encoded);
                lineLength++;
            }

            if (lineLength < 128) continue;
            WriteAscii(output, "\r\n");
            lineLength = 0;
        }

        if (lineLength > 0) WriteAscii(output, "\r\n");
        WriteAscii(output, $"=yend size={source.Length}\r\n");
        return output.ToArray();
    }

    private static void WriteAscii(Stream output, string value)
    {
        output.Write(Encoding.ASCII.GetBytes(value));
    }
}

[MemoryDiagnoser]
public class SegmentStreamBenchmarks
{
    private const int SegmentSize = 256 * 1024;
    private BenchmarkNntpClient _client = null!;
    private string[] _segmentIds = null!;
    private LongRange[] _segmentRanges = null!;

    [Params(0, 4)]
    public int ArticleBufferSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var segments = Enumerable.Range(0, 8).ToDictionary(
            index => $"segment-{index}",
            index =>
            {
                var bytes = new byte[SegmentSize];
                new Random(index).NextBytes(bytes);
                return bytes;
            });
        _segmentIds = segments.Keys.ToArray();
        _segmentRanges = Enumerable.Range(0, segments.Count)
            .Select(index => new LongRange(
                index * SegmentSize, (index + 1L) * SegmentSize))
            .ToArray();
        _client = new BenchmarkNntpClient(segments);
    }

    [Benchmark]
    public async Task ReadSegmentStream()
    {
        await using var stream = new NzbFileStream(
            _segmentIds,
            (long)_segmentIds.Length * SegmentSize,
            _client,
            ArticleBufferSize,
            _segmentRanges);
        await stream.CopyToAsync(Stream.Null);
    }
}

internal sealed class BenchmarkNntpClient(
    IReadOnlyDictionary<string, byte[]> segments) : NntpClient
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
        var response = CreateResponse(segmentId);
        onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        return Task.FromResult(response);
    }

    public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken)
    {
        var responses = segmentIds
            .Select(segmentId => DecodedBodyAsync(segmentId, cancellationToken))
            .ToArray();
        onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        return Task.FromResult(new UsenetDecodedBodyBatch { Responses = responses });
    }

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
        DecodedBodiesAsync(
            segmentIds, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override void Dispose()
    {
    }

    private UsenetDecodedBodyResponse CreateResponse(SegmentId segmentId)
    {
        var key = segmentId.ToString();
        return new UsenetDecodedBodyResponse
        {
            SegmentId = key,
            ResponseCode = (int)UsenetResponseType.ArticleRetrievedBodyFollows,
            ResponseMessage = "222 benchmark body",
            Stream = new YencStream(new MemoryStream(
                YencDecodeBenchmarks.EncodeYenc(segments[key]), writable: false))
        };
    }
}
