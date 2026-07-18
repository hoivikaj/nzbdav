using System.IO.Compression;

namespace NzbWebDAV.Utils;

public static class NzbStreamUtil
{
    public sealed record OpenResult(Stream Stream, bool IsGzip);

    public static async ValueTask<OpenResult> OpenMaybeCompressedAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        var prefix = new byte[2];
        var prefixLength = 0;
        while (prefixLength < prefix.Length)
        {
            var read = await source.ReadAsync(
                    prefix.AsMemory(prefixLength, prefix.Length - prefixLength),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            prefixLength += read;
        }

        var replay = new PrefixReadStream(source, prefix, prefixLength);
        var isGzip = prefixLength == 2 && prefix[0] == 0x1f && prefix[1] == 0x8b;
        return isGzip
            ? new OpenResult(
                new GZipStream(replay, CompressionMode.Decompress, leaveOpen: false),
                IsGzip: true)
            : new OpenResult(replay, IsGzip: false);
    }

    public static string NormalizeFileName(string fileName)
    {
        var normalized = fileName.Trim();
        if (normalized.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^3];
        if (!normalized.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
            normalized += ".nzb";
        return normalized;
    }

    private sealed class PrefixReadStream(
        Stream source,
        byte[] prefix,
        int prefixLength) : Stream
    {
        private int _prefixPosition;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = CopyPrefix(buffer.AsSpan(offset, count));
            return copied > 0 ? copied : source.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            var copied = CopyPrefix(buffer);
            return copied > 0 ? copied : source.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var copied = CopyPrefix(buffer.Span);
            return copied > 0
                ? copied
                : await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        private int CopyPrefix(Span<byte> destination)
        {
            var remaining = prefixLength - _prefixPosition;
            if (remaining <= 0 || destination.IsEmpty)
                return 0;
            var count = Math.Min(remaining, destination.Length);
            prefix.AsSpan(_prefixPosition, count).CopyTo(destination);
            _prefixPosition += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
