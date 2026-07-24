namespace NzbWebDAV.Clients.Usenet.Contexts;

/// <summary>
/// Marks an NNTP cancellation token as belonging to a queue import worker.
/// Primary workers get preferred admission on the queue soft semaphore; secondary
/// workers use the Low lane and share spare capacity. Does not change physical
/// pool priority — queue BODY requests remain Low at the provider pool.
/// </summary>
public sealed class QueueDownloadContext
{
    private long _semaphoreWaitMilliseconds;

    /// <summary>
    /// True while this worker is the preferred (first) active queue item.
    /// Mutated in place when a secondary is promoted after the previous primary finishes.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Per-phase task fan-out for this worker. Re-evaluated each phase so a
    /// promoted secondary immediately gets the primary budget.
    /// </summary>
    public required Func<int> GetFanOutConcurrency { get; init; }

    public long SemaphoreWaitMilliseconds => Interlocked.Read(ref _semaphoreWaitMilliseconds);

    public void RecordSemaphoreWait(long elapsedMilliseconds)
    {
        if (elapsedMilliseconds > 0)
            Interlocked.Add(ref _semaphoreWaitMilliseconds, elapsedMilliseconds);
    }
}
