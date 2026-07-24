namespace NzbWebDAV.Services;

internal static class WardenInputLimits
{
    internal const long MaxDecompressedBytes = 512L * 1024 * 1024;
    internal const int MaxRecordCharacters = 64 * 1024;
    internal const int MaxRecords = 4_000_000;
}

internal sealed class LimitedReadStream(Stream inner, long maximumBytes) : Stream
{
    private long _read;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => ReadAndCountAsync(buffer, ct);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read)
    {
        if (read > 0 && _read > maximumBytes - read)
            throw new InvalidOperationException($"Warden source exceeds the {maximumBytes:N0}-byte decompressed limit.");
        _read += read;
        return read;
    }

    private async ValueTask<int> ReadAndCountAsync(Memory<byte> buffer, CancellationToken ct) =>
        Count(await inner.ReadAsync(buffer, ct).ConfigureAwait(false));
}
