namespace NzbWebDAV.Database.Models.Metrics;

public class ReadSession
{
    public Guid Id { get; set; }
    public long StartedAt { get; set; }
    public long EndedAt { get; set; }
    public int DurationMs { get; set; }
    public string Path { get; set; } = null!;
    public long? FileSize { get; set; }
    public long BytesServed { get; set; }
    public long BytesFetched { get; set; }
    public int FailoverSaves { get; set; }
    public string? ClientUserAgent { get; set; }
    public string? ClientIp { get; set; }
    public EndReasonCode EndReason { get; set; }

    public enum EndReasonCode
    {
        Completed = 0,
        Aborted = 1,
        Timeout = 2,
        Error = 3,
    }
}
