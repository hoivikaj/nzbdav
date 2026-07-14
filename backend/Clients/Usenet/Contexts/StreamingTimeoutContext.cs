namespace NzbWebDAV.Clients.Usenet.Contexts;

/// <summary>
/// Attached to WebDAV /view streaming cancellation tokens so segment fetches
/// fail fast and retry on a fresh connection instead of waiting for UsenetSharp's
/// ~40s internal read timeout. Not set for queue or health-check traffic.
/// </summary>
public record StreamingTimeoutContext
{
    public required TimeSpan PerSegmentTimeout { get; init; }
    public required int MaxRetries { get; init; }
}
