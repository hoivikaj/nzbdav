using NzbWebDAV.Clients.Usenet.Concurrency;

namespace NzbWebDAV.Clients.Usenet.Contexts;

public record DownloadPriorityContext
{
    public required SemaphorePriority Priority { get; init; }

    /// <summary>
    /// When set, streaming reads belonging to this playback session acquire from
    /// this per-stream semaphore instead of the shared global streaming semaphore.
    /// Populated when the "per stream" max-download-connections mode is enabled so
    /// each concurrent stream gets its own connection budget.
    /// </summary>
    public PrioritizedSemaphore? StreamSemaphore { get; init; }
}
