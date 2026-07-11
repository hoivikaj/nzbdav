namespace NzbWebDAV.Database.Models;

public class ListSource
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Url { get; set; }

    public bool Enabled { get; set; } = true;

    public int Cap { get; set; }

    public string? SeriesScope { get; set; }

    public long CreatedAtUnix { get; set; }
    public long? LastSyncedAtUnix { get; set; }
    public string? LastSyncError { get; set; }

    public const string KindManual = "manual";
    public const string KindStremioCatalog = "stremio-catalog";
    public const string KindUrlList = "url-list";
}
