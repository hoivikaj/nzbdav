namespace NzbWebDAV.Database.Models;

public class WantedItem
{
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string ContentId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string State { get; set; } = StateScouting;

    public string Provenance { get; set; } = "[]";

    public string Shortlist { get; set; } = "[]";

    public byte[]? WinnerNzb { get; set; }

    public string? ResponderHost { get; set; }
    public string? FailReason { get; set; }

    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
    public long? LastResolvedAtUnix { get; set; }
    public long? LastVerifiedAtUnix { get; set; }

    public long? NextCheckAtUnix { get; set; }

    public const string StateScouting = "scouting";
    public const string StateReady = "ready";
    public const string StateUnavailable = "unavailable";
    public const string StateParked = "parked";
    public const string StateExpander = "expander";

    public static bool IsBareSeries(string type, string contentId)
        => type == "series" && contentId.Split(':').Length < 3;
}
