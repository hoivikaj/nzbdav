namespace NzbWebDAV.Database.Models.Metrics;

public class MetricEvent
{
    public long Id { get; set; }
    public long At { get; set; }
    public string Kind { get; set; } = null!;
    public string? RefId { get; set; }
    public string? Tag1 { get; set; }
    public string? Tag2 { get; set; }
    public long? Num { get; set; }
    public string? Note { get; set; }
}
