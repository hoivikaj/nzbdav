using System.Collections.Concurrent;

namespace NzbWebDAV.Services;

/// <summary>
/// In-memory marker for queue items that originated from the profile/manifest
/// flow (ProfilePlayController). Those items already get a detailed playback
/// attempt log entry written by the profile flow; the queue processor uses
/// this to avoid double-logging on completion. Items not present here are
/// treated as external SAB submissions (third-party SAB-compatible clients, Sonarr, etc.)
/// and get a single completion entry written by the queue processor.
/// </summary>
public class QueueItemSourceTracker
{
    private readonly ConcurrentDictionary<Guid, byte> _profileFlowItems = new();

    public void MarkAsProfileFlow(Guid queueItemId)
    {
        _profileFlowItems[queueItemId] = 0;
    }

    /// <summary>
    /// Consume the marker. Returns true if the item was previously marked as
    /// profile-flow; removes it from the set either way.
    /// </summary>
    public bool ConsumeIsProfileFlow(Guid queueItemId)
    {
        return _profileFlowItems.TryRemove(queueItemId, out _);
    }
}
