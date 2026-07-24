using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

/// <summary>
/// In-memory counter of consecutive permanent streaming failures (missing usenet articles
/// or structurally corrupt archives) per
/// <c>DavItem</c>. Incremented by <c>ExceptionMiddleware</c> whenever it observes a qualifying
/// failure; consulted by urgent-repair scheduling and cleared after a successful full read,
/// health check, repair, or deletion.
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

    /// <summary>Clears the counter after a successful full read, health check, repair, or deletion.</summary>
    public void ClearFailure(Guid davItemId)
    {
        _failureCounts.TryRemove(davItemId, out _);
    }
}
