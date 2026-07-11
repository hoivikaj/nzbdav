namespace NzbWebDAV.Database.Models.Metrics;

public class FailoverHourly
{
    public long Hour { get; set; }
    public string FromProvider { get; set; } = null!;
    public string ToProvider { get; set; } = null!;
    public SegmentFetch.FetchStatus Reason { get; set; }
    public long Count { get; set; }
}
