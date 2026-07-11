using System.Collections.Concurrent;
using NzbWebDAV.Config;

namespace NzbWebDAV.Services;

public class PreflightCache(ConfigManager configManager)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private DateTimeOffset _lastSweep = DateTimeOffset.UtcNow;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    public Entry? Get(string nzbUrl)
    {
        if (!_entries.TryGetValue(nzbUrl, out var entry)) return null;
        if (DateTimeOffset.UtcNow > entry.ExpiresAt)
        {
            _entries.TryRemove(nzbUrl, out _);
            return null;
        }
        return entry;
    }

    public void SetVerified(string nzbUrl, byte[]? nzbBytes, PlaybackFastVerifier.Verdict verdict, string? responderHost)
    {
        var ttl = TimeSpan.FromSeconds(configManager.GetPreflightTtlSeconds());
        _entries[nzbUrl] = new Entry
        {
            NzbBytes = nzbBytes,
            Verdict = verdict,
            ResponderHost = responderHost,
            PreparedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + ttl,
        };
        SweepIfDue();
    }

    public void Invalidate(string nzbUrl) => _entries.TryRemove(nzbUrl, out _);

    private void SweepIfDue()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSweep < SweepInterval) return;
        _lastSweep = now;
        foreach (var kv in _entries)
        {
            if (now > kv.Value.ExpiresAt) _entries.TryRemove(kv.Key, out _);
        }
    }

    public class Entry
    {
        public required byte[]? NzbBytes { get; init; }
        public required PlaybackFastVerifier.Verdict Verdict { get; init; }
        public required string? ResponderHost { get; init; }
        public required DateTimeOffset PreparedAt { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
