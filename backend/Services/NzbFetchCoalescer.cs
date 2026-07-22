using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

public class NzbFetchCoalescer
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan HardFetchCap = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<FetchKey, Lazy<Task<byte[]?>>> _inFlight = new();
    private readonly ConcurrentDictionary<FetchKey, Entry> _cache = new();
    private DateTimeOffset _lastSweep = DateTimeOffset.UtcNow;

    public async Task<byte[]?> GetOrFetchAsync(
        string url,
        string? proxyUrl,
        bool skipTlsVerification,
        Func<CancellationToken, Task<byte[]?>> fetch,
        CancellationToken ct)
    {
        var key = new FetchKey(url, proxyUrl ?? "", skipTlsVerification);
        if (TryGetCached(key, out var cached)) return cached;

        var lazy = _inFlight.GetOrAdd(key, k => new Lazy<Task<byte[]?>>(() => RunSharedAsync(k, fetch)));
        return await lazy.Value.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<byte[]?> RunSharedAsync(FetchKey key, Func<CancellationToken, Task<byte[]?>> fetch)
    {
        try
        {
            using var cts = new CancellationTokenSource(HardFetchCap);
            var bytes = await fetch(cts.Token).ConfigureAwait(false);
            if (bytes is not null)
            {
                _cache[key] = new Entry(bytes, DateTimeOffset.UtcNow + CacheTtl);
                SweepIfDue();
            }
            return bytes;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private bool TryGetCached(FetchKey key, out byte[]? bytes)
    {
        bytes = null;
        if (!_cache.TryGetValue(key, out var e)) return false;
        if (DateTimeOffset.UtcNow > e.ExpiresAt)
        {
            _cache.TryRemove(key, out _);
            return false;
        }
        bytes = e.Bytes;
        return true;
    }

    private void SweepIfDue()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSweep < SweepInterval) return;
        _lastSweep = now;
        foreach (var kv in _cache)
            if (now > kv.Value.ExpiresAt) _cache.TryRemove(kv.Key, out _);
    }

    private readonly record struct Entry(byte[] Bytes, DateTimeOffset ExpiresAt);
    private readonly record struct FetchKey(string Url, string ProxyUrl, bool SkipTlsVerification);
}
