namespace NzbWebDAV.Database.Models;

public class IndexerApiHit
{
    public long Id { get; set; }
    public string IndexerName { get; set; } = null!;
    public HitType Type { get; set; }
    public DateTimeOffset AccessedAt { get; set; }

    public enum HitType
    {
        Search = 0,
        Download = 1,
    }
}
