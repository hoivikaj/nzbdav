using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Exceptions;
using Serilog;
using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

public class UnbufferedMultiSegmentStream : FastReadOnlyNonSeekableStream
{
    private readonly Memory<string> _segmentIds;
    private readonly INntpClient _usenetClient;
    private readonly long _expectedSegmentSize;
    private Stream? _stream;
    private int _currentIndex;
    private bool _disposed;


    public UnbufferedMultiSegmentStream(Memory<string> segmentIds, INntpClient usenetClient, long expectedSegmentSize)
    {
        _segmentIds = segmentIds;
        _usenetClient = usenetClient;
        _expectedSegmentSize = expectedSegmentSize;
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
                var segmentId = _segmentIds.Span[_currentIndex++];
                try
                {
                    var body = await _usenetClient.DecodedBodyAsync(segmentId, cancellationToken);
                    _stream = body.Stream!;
                }
                catch (UsenetArticleNotFoundException e)
                {
                    var fill = _expectedSegmentSize > 0 ? _expectedSegmentSize : 1;
                    Log.Warning(
                        "Article {SegmentId} missing on all providers. Zero-filling {Bytes} bytes to keep playback alive.",
                        e.SegmentId, fill);
                    _stream = new MemoryStream(new byte[fill], writable: false);
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
