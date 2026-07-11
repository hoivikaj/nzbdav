namespace NzbWebDAV.Database.Models.Metrics;

public class FailoverMiss
{
    public long Id { get; set; }
    public long At { get; set; }
    public string FromProvider { get; set; } = null!;
    public string ToProvider { get; set; } = null!;
    public SegmentFetch.FetchStatus Reason { get; set; }
}
