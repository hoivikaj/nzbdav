using System.Collections.Concurrent;
using NzbWebDAV.Config;

namespace NzbWebDAV.Services;

public class CandidateNegativeCache
{
    private readonly ConfigManager _configManager;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failedAt = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _brokenHistoryAt = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _brokenFileNameAt =
        new(StringComparer.OrdinalIgnoreCase);

    public CandidateNegativeCache(ConfigManager configManager)
    {
        _configManager = configManager;
    }

    public bool IsFailed(string nzbUrl)
    {
        if (!_failedAt.TryGetValue(nzbUrl, out var failedAt)) return false;
        var ttl = _configManager.GetPlayCandidateNegativeCacheTtl();
        if (DateTimeOffset.UtcNow - failedAt < ttl) return true;
        _failedAt.TryRemove(nzbUrl, out _);
        return false;
    }

    public void MarkFailed(string nzbUrl)
    {
        _failedAt[nzbUrl] = DateTimeOffset.UtcNow;
        if (_failedAt.Count > 512) Cleanup();
    }

    public bool IsHistoryItemBroken(Guid historyItemId)
    {
        if (!_brokenHistoryAt.TryGetValue(historyItemId, out var brokenAt)) return false;
        var ttl = _configManager.GetPlayCandidateNegativeCacheTtl();
        if (DateTimeOffset.UtcNow - brokenAt < ttl) return true;
        _brokenHistoryAt.TryRemove(historyItemId, out _);
        return false;
    }

    public void MarkHistoryItemBroken(Guid historyItemId)
    {
        _brokenHistoryAt[historyItemId] = DateTimeOffset.UtcNow;
        if (_brokenHistoryAt.Count > 512) CleanupHistory();
    }

    public bool IsFileNameBroken(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        if (!_brokenFileNameAt.TryGetValue(fileName, out var brokenAt)) return false;
        var ttl = _configManager.GetPlayCandidateNegativeCacheTtl();
        if (DateTimeOffset.UtcNow - brokenAt < ttl) return true;
        _brokenFileNameAt.TryRemove(fileName, out _);
        return false;
    }

    public void MarkFileNameBroken(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        _brokenFileNameAt[fileName] = DateTimeOffset.UtcNow;
        if (_brokenFileNameAt.Count > 512) CleanupFileNames();
    }

    public IReadOnlyCollection<Guid> SnapshotBrokenHistoryItems()
    {
        var ttl = _configManager.GetPlayCandidateNegativeCacheTtl();
        var cutoff = DateTimeOffset.UtcNow - ttl;
        return _brokenHistoryAt
            .Where(kv => kv.Value >= cutoff)
            .Select(kv => kv.Key)
            .ToList();
    }

    private void Cleanup()
    {
        var ttl = _configManager.GetPlayCandidateNegativeCacheTtl();
        var cutoff = DateTimeOffset.UtcNow - ttl;
        foreach (var kv in _failedAt)
            if (kv.Value < cutoff) _failedAt.TryRemove(kv.Key, out _);
    }

    private void CleanupHistory()
    {
        var ttl = _configManager.GetPlayCandidateNegativeCacheTtl();
        var cutoff = DateTimeOffset.UtcNow - ttl;
        foreach (var kv in _brokenHistoryAt)
            if (kv.Value < cutoff) _brokenHistoryAt.TryRemove(kv.Key, out _);
    }

    private void CleanupFileNames()
    {
        var ttl = _configManager.GetPlayCandidateNegativeCacheTtl();
        var cutoff = DateTimeOffset.UtcNow - ttl;
        foreach (var kv in _brokenFileNameAt)
            if (kv.Value < cutoff) _brokenFileNameAt.TryRemove(kv.Key, out _);
    }
}
