namespace NzbWebDAV.Database.Models.Metrics;

public class CatalogueDaily
{
    public long Day { get; set; }
    public long FileCount { get; set; }
    public long TotalBytes { get; set; }
    public long AddedCount { get; set; }
    public long RemovedCount { get; set; }
}
