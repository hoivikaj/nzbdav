using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Services.StreamTrace;
using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

/// <summary>
/// Caps a stream at a declared length and fills a premature shortfall with
/// zeros so subsequent multipart data retains its expected byte offsets.
/// </summary>
public sealed class PaddedLengthStream(
    Stream stream,
    long length,
    string partId,
    string? fileName = null) : FastReadOnlyNonSeekableStream
{
    private readonly string _fileName = string.IsNullOrEmpty(fileName) ? "unknown" : fileName;
    private long _position;
    private bool _underlyingEnded;
    private bool _shortfallReported;
    private bool _disposed;

    public override long Length => length;
    public override long Position => _position;

    public override void Flush() => stream.Flush();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty || _position >= length)
            return 0;

        var bytesToRead = (int)Math.Min(length - _position, buffer.Length);
        if (!_underlyingEnded)
        {
            var bytesRead = await stream.ReadAsync(buffer[..bytesToRead], cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead > 0)
            {
                _position += bytesRead;
                return bytesRead;
            }

            _underlyingEnded = true;
            ReportShortfall(length - _position);
        }

        buffer.Span[..bytesToRead].Clear();
        _position += bytesToRead;
        return bytesToRead;
    }

    private void ReportShortfall(long bytes)
    {
        if (_shortfallReported)
            return;

        _shortfallReported = true;
        ZeroFillLogLimiter.Write(
            "Packed part {SegmentId} ended early while reading {FileName}. Zero-filling {Bytes} bytes to preserve multipart offsets.",
            partId,
            _fileName,
            bytes);

        if (MultiProviderNntpClient.CurrentReadSessionId is { } sessionId)
            StreamTrace.TryZeroFill(sessionId, partId, bytes);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
            return;

        stream.Dispose();
        _disposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await stream.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
