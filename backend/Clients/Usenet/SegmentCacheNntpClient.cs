using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Streams;
using Serilog;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Clients.Usenet;

public sealed class SegmentCacheNntpClient : WrappingNntpClient
{
    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly ConcurrentDictionary<string, CacheEntry> _index = new();
    private readonly object _evictLock = new();
    private long _currentBytes;

    private static readonly JsonSerializerOptions HeaderJsonOptions = new() { IncludeFields = true };

    public SegmentCacheNntpClient(INntpClient inner, string cacheDir, long maxBytes) : base(inner)
    {
        _dir = cacheDir;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(_dir);
        LoadIndex();
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken ct)
    {
        return DecodedBodyAsync(segmentId, onConnectionReadyAgain: null, ct);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken ct)
    {
        if (MultiProviderNntpClient.AttributionContext.Value != null)
            return await base.DecodedBodyAsync(segmentId, onConnectionReadyAgain, ct).ConfigureAwait(false);

        string id = segmentId;
        if (TryServeFromCache(id, out var cached))
        {
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
            return cached!;
        }

        var response = await base.DecodedBodyAsync(segmentId, onConnectionReadyAgain, ct).ConfigureAwait(false);
        return await WrapForCachingAsync(id, response, ct).ConfigureAwait(false);
    }

    public override async Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        string segmentId, CancellationToken ct)
    {
        if (MultiProviderNntpClient.AttributionContext.Value == null && _index.ContainsKey(Hash(segmentId)))
            return new UsenetExclusiveConnection(onConnectionReadyAgain: null);
        return await base.AcquireExclusiveConnectionAsync(segmentId, ct).ConfigureAwait(false);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, UsenetExclusiveConnection exclusiveConnection, CancellationToken ct)
    {
        if (MultiProviderNntpClient.AttributionContext.Value != null)
            return await base.DecodedBodyAsync(segmentId, exclusiveConnection, ct).ConfigureAwait(false);

        string id = segmentId;
        if (TryServeFromCache(id, out var cached))
        {
            exclusiveConnection.OnConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
            return cached!;
        }

        var response = await base.DecodedBodyAsync(segmentId, exclusiveConnection, ct).ConfigureAwait(false);
        return await WrapForCachingAsync(id, response, ct).ConfigureAwait(false);
    }

    private async Task<UsenetDecodedBodyResponse> WrapForCachingAsync(
        string id, UsenetDecodedBodyResponse response, CancellationToken ct)
    {
        if (response.ResponseType != UsenetResponseType.ArticleRetrievedBodyFollows)
            return response;

        UsenetYencHeader? header = null;
        try
        {
            header = await response.Stream.GetYencHeadersAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            header = null;
        }

        if (header == null) return response;
        return response with { Stream = new WriteThroughStream(response.Stream, header, BlobPath(Hash(id)), OnFinalized) };
    }

    private bool TryServeFromCache(string id, out UsenetDecodedBodyResponse? response)
    {
        response = null;
        var hash = Hash(id);
        if (!_index.TryGetValue(hash, out var entry)) return false;

        var blobPath = BlobPath(hash);
        try
        {
            var header = JsonSerializer.Deserialize<UsenetYencHeader>(
                File.ReadAllText(blobPath + ".h"), HeaderJsonOptions);
            if (header == null || header.PartSize != entry.Size)
            {
                Drop(hash);
                return false;
            }

            var fileStream = new FileStream(blobPath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, bufferSize: 81920, useAsync: true);
            entry.LastAccessTicks = DateTime.UtcNow.Ticks;
            response = new UsenetDecodedBodyResponse
            {
                SegmentId = id,
                ResponseCode = (int)UsenetResponseType.ArticleRetrievedBodyFollows,
                ResponseMessage = "222 - Article retrieved from segment cache",
                Stream = new CachedYencStream(header, fileStream),
            };
            return true;
        }
        catch
        {
            Drop(hash);
            return false;
        }
    }

    private void OnFinalized(string hash, long size)
    {
        lock (_evictLock)
        {
            if (_index.TryGetValue(hash, out var existing)) _currentBytes -= existing.Size;
            _index[hash] = new CacheEntry { Size = size, LastAccessTicks = DateTime.UtcNow.Ticks };
            _currentBytes += size;
        }

        EvictIfNeeded();
    }

    private void Drop(string hash)
    {
        lock (_evictLock)
        {
            if (_index.TryRemove(hash, out var entry)) _currentBytes -= entry.Size;
        }

        SafeDelete(BlobPath(hash));
        SafeDelete(BlobPath(hash) + ".h");
    }

    private void EvictIfNeeded()
    {
        if (_currentBytes <= _maxBytes) return;
        lock (_evictLock)
        {
            if (_currentBytes <= _maxBytes) return;
            foreach (var kv in _index.OrderBy(x => x.Value.LastAccessTicks).ToList())
            {
                if (_currentBytes <= _maxBytes) break;
                if (!_index.TryRemove(kv.Key, out var entry)) continue;
                _currentBytes -= entry.Size;
                SafeDelete(BlobPath(kv.Key));
                SafeDelete(BlobPath(kv.Key) + ".h");
            }
        }
    }

    private void LoadIndex()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    SafeDelete(file);
                    continue;
                }

                if (file.EndsWith(".h", StringComparison.Ordinal)) continue;
                var info = new FileInfo(file);
                if (!info.Exists) continue;
                _index[Path.GetFileName(file)] = new CacheEntry
                {
                    Size = info.Length,
                    LastAccessTicks = info.LastWriteTimeUtc.Ticks,
                };
                _currentBytes += info.Length;
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, "Segment cache: failed to scan {Dir}; starting empty.", _dir);
        }

        EvictIfNeeded();
    }

    private string BlobPath(string hash) => Path.Combine(_dir, hash[..2], hash);

    private static string Hash(string id)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private sealed class CacheEntry
    {
        public long Size;
        public long LastAccessTicks;
    }

    private sealed class WriteThroughStream : YencStream
    {
        private readonly YencStream _source;
        private readonly UsenetYencHeader _header;
        private readonly string _blobPath;
        private readonly string _tempPath;
        private readonly Action<string, long> _onFinalized;
        private FileStream? _temp;
        private long _written;
        private bool _eof;
        private bool _writeFailed;

        public WriteThroughStream(YencStream source, UsenetYencHeader header, string blobPath,
            Action<string, long> onFinalized) : base(Null)
        {
            _source = source;
            _header = header;
            _blobPath = blobPath;
            _tempPath = blobPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            _onFinalized = onFinalized;
        }

        public override ValueTask<UsenetYencHeader?> GetYencHeadersAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<UsenetYencHeader?>(_header);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await _source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (n > 0)
            {
                if (!_writeFailed)
                {
                    try
                    {
                        _temp ??= OpenTemp();
                        await _temp.WriteAsync(buffer[..n], cancellationToken).ConfigureAwait(false);
                        _written += n;
                    }
                    catch
                    {
                        _writeFailed = true;
                    }
                }
            }
            else
            {
                _eof = true;
            }

            return n;
        }

        private FileStream OpenTemp()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_blobPath)!);
            return new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _source.Dispose();
                try
                {
                    _temp?.Dispose();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    if (_eof && !_writeFailed && _temp != null && _written == _header.PartSize)
                    {
                        File.WriteAllText(_blobPath + ".h", JsonSerializer.Serialize(_header, HeaderJsonOptions));
                        File.Move(_tempPath, _blobPath, overwrite: true);
                        _onFinalized(Path.GetFileName(_blobPath), _written);
                    }
                    else
                    {
                        SafeDelete(_tempPath);
                    }
                }
                catch
                {
                    SafeDelete(_tempPath);
                    SafeDelete(_blobPath + ".h");
                }
            }

            base.Dispose(disposing);
        }
    }
}
