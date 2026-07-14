using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

/// <summary>
/// In-memory counter of consecutive streaming failures (missing usenet articles) per
/// <c>DavItem</c>. Incremented by <c>ExceptionMiddleware</c> whenever it schedules an urgent
/// repair for an item; consulted (and cleared) by <c>HealthCheckService</c> to power the
/// opt-in "auto-remove after N failures" repair policy (see <c>repair.auto-remove-after-failures</c>).
///
/// Deliberately in-memory rather than persisted: failures recur naturally on replay, so a
/// process restart simply resets the count, which is an acceptable trade-off for avoiding a
/// schema migration for a niche opt-in feature.
/// </summary>
public class StreamingFailureTracker
{
    private readonly ConcurrentDictionary<Guid, int> _failureCounts = new();

    /// <summary>Increments and returns the new failure count for the item.</summary>
    public int RecordFailure(Guid davItemId)
    {
        return _failureCounts.AddOrUpdate(davItemId, 1, (_, count) => count + 1);
    }

    /// <summary>Returns the current failure count for the item (0 if never recorded).</summary>
    public int GetFailureCount(Guid davItemId)
    {
        return _failureCounts.GetValueOrDefault(davItemId);
    }

    /// <summary>Clears the counter, e.g. after a successful health check or once the item is repaired/removed.</summary>
    public void ClearFailure(Guid davItemId)
    {
        _failureCounts.TryRemove(davItemId, out _);
    }
}
