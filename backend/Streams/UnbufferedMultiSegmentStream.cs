using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Services.StreamTrace;
using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

public class UnbufferedMultiSegmentStream : FastReadOnlyNonSeekableStream
{
    private const int MaxConsecutiveZeroFills = 3;

    private readonly Memory<string> _segmentIds;
    private readonly string[][]? _segmentFallbacks;
    private readonly INntpClient _usenetClient;
    private readonly long _expectedSegmentSize;
    private readonly string _fileName;
    private Stream? _stream;
    private int _currentIndex;
    private int _consecutiveZeroFills;
    private bool _disposed;


    public UnbufferedMultiSegmentStream(
        Memory<string> segmentIds,
        INntpClient usenetClient,
        long expectedSegmentSize,
        string? fileName = null,
        string[][]? segmentFallbacks = null)
    {
        _segmentIds = segmentIds;
        _segmentFallbacks = segmentFallbacks;
        _usenetClient = usenetClient;
        _expectedSegmentSize = expectedSegmentSize;
        _fileName = string.IsNullOrEmpty(fileName) ? "unknown" : fileName;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // if the stream is null, get the next stream.
            if (_stream == null)
            {
                if (_currentIndex >= _segmentIds.Length) return 0;
                var segmentIndex = _currentIndex;
                var segmentId = _segmentIds.Span[_currentIndex++];
                try
                {
                    var body = await _usenetClient.DecodedBodyAsync(segmentId, cancellationToken);
                    _stream = body.Stream!;
                    _consecutiveZeroFills = 0;
                }
                catch (UsenetArticleNotFoundException e)
                {
                    var fallback = await TryFallbackSegmentsAsync(segmentIndex, cancellationToken)
                        .ConfigureAwait(false);
                    if (fallback is not null)
                    {
                        _stream = fallback;
                        _consecutiveZeroFills = 0;
                    }
                    else
                    {
                        var fill = _expectedSegmentSize > 0 ? _expectedSegmentSize : 1;
                        _consecutiveZeroFills++;
                        ZeroFillLogLimiter.Write(
                            "Article {SegmentId} missing on all providers while reading {FileName}. Zero-filling {Bytes} bytes to keep playback alive.",
                            e.SegmentId,
                            _fileName,
                            fill,
                            e);
                        if (MultiProviderNntpClient.CurrentReadSessionId is { } sessionId)
                            StreamTrace.TryZeroFill(sessionId, e.SegmentId, fill);
                        if (_consecutiveZeroFills >= MaxConsecutiveZeroFills)
                            throw;

                        _stream = new MemoryStream(new byte[fill], writable: false);
                    }
                }
            }

            // read from the stream
            var read = await _stream.ReadAsync(buffer, cancellationToken);
            if (read > 0) return read;

            // if the stream ended, continue to the next stream.
            await _stream.DisposeAsync();
            _stream = null;
        }
    }

    private async Task<Stream?> TryFallbackSegmentsAsync(
        int segmentIndex,
        CancellationToken cancellationToken)
    {
        if (_segmentFallbacks is null ||
            segmentIndex < 0 ||
            segmentIndex >= _segmentFallbacks.Length)
            return null;

        var fallbacks = _segmentFallbacks[segmentIndex] ?? [];
        foreach (var fallbackId in fallbacks)
        {
            try
            {
                var body = await _usenetClient
                    .DecodedBodyAsync(fallbackId, cancellationToken)
                    .ConfigureAwait(false);
                return body.Stream!;
            }
            catch (UsenetArticleNotFoundException)
            {
                // Try the next alternate MessageId.
            }
        }

        return null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (!disposing) return;
        _disposed = true;
        _stream?.Dispose();
        base.Dispose();
    }
}
