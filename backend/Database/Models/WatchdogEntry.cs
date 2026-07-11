namespace NzbWebDAV.Database.Models;

public class WatchdogEntry
{
    public long Id { get; set; }
    public Guid ClickId { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public string ContentType { get; set; } = null!;
    public string RequestedTitle { get; set; } = null!;
    public string CandidateTitle { get; set; } = null!;
    public string IndexerName { get; set; } = null!;
    public long Size { get; set; }
    public int RankIndex { get; set; }
    public Outcome Result { get; set; }
    public string? FailReason { get; set; }
    public int DurationMs { get; set; }
    public bool IsWinner { get; set; }
    public string? ProviderHost { get; set; }

    // Cascade-cleanup links. Either or both may be set depending on call site:
    // QueueItemId is known when the queue processor records a final outcome;
    // ContentGroupKey is known when the play controller records candidates.
    public Guid? QueueItemId { get; set; }
    public string? ContentGroupKey { get; set; }

    public enum Outcome
    {
        PreVerifyAvailable,
        PreVerifyDead,
        PreVerifyTimeout,
        Cancelled,
        EnqueueFailed,
        QueueFailed,
        QueueCompleted,
        BudgetTimeout,
        ExcludedByPattern,
    }
}
