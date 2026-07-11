using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

/// <summary>
/// In-memory per-queue-item counter of which Usenet provider served each segment.
/// Scope is propagated via AsyncLocal so deep async calls inside QueueItemProcessor
/// pick up the current queue-item id without threading parameters through every layer.
/// </summary>
public class ProviderUsageTracker(ActiveReadRegistry? activeReadRegistry = null)
{
    private static readonly AsyncLocal<Guid?> CurrentScope = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, long>> _usage = new();
    private readonly ConcurrentDictionary<Guid, long> _failoverSaves = new();

    public IDisposable BeginScope(Guid queueItemId)
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = queueItemId;
        return new Releaser(() => CurrentScope.Value = previous);
    }

    public void RecordSuccess(string providerHost)
    {
        var qid = CurrentScope.Value;
        if (qid == null || string.IsNullOrEmpty(providerHost)) return;
        var counts = _usage.GetOrAdd(qid.Value, _ => new ConcurrentDictionary<string, long>());
        counts.AddOrUpdate(providerHost, 1, (_, v) => v + 1);
        // Keep the active-read entry alive while NNTP fetches are flowing for
        // this scope. No-op when the scope id isn't a registered read session.
        activeReadRegistry?.Touch(qid.Value, 0);
    }

    public void RecordFailoverSave()
    {
        var id = CurrentScope.Value;
        if (id == null) return;
        _failoverSaves.AddOrUpdate(id.Value, 1, (_, v) => v + 1);
    }

    public long GetFailoverSaves(Guid scopeId)
        => _failoverSaves.TryGetValue(scopeId, out var v) ? v : 0;

    public IReadOnlyDictionary<string, long> Snapshot(Guid queueItemId)
    {
        if (!_usage.TryGetValue(queueItemId, out var d)) return new Dictionary<string, long>();
        return d.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, long>> SnapshotMany(IEnumerable<Guid> ids)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<string, long>>();
        foreach (var id in ids)
        {
            if (_usage.TryGetValue(id, out var d))
                result[id] = d.ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        return result;
    }

    public void Clear(Guid queueItemId)
    {
        _usage.TryRemove(queueItemId, out _);
        _failoverSaves.TryRemove(queueItemId, out _);
    }

    private sealed class Releaser(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
