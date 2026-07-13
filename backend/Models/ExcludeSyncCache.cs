using System.Text.Json.Serialization;

namespace NzbWebDAV.Models;

/// <summary>
/// Persisted last-good snapshot of synced exclude patterns, keyed by source URL.
/// Stored as JSON under the <c>search.exclude-sync-cache</c> config key so the filter
/// survives upstream outages and server restarts (the fail-safe fallback).
/// </summary>
public sealed class ExcludeSyncCache
{
    [JsonPropertyName("urls")]
    public Dictionary<string, ExcludeSyncUrlEntry> Urls { get; set; } = new();
}

public sealed class ExcludeSyncUrlEntry
{
    /// <summary>Pattern strings from the last successful fetch of this URL.</summary>
    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = new();

    /// <summary>Unix seconds of the last successful fetch (HTTP 200 with a parseable body).</summary>
    [JsonPropertyName("fetchedAt")]
    public long FetchedAt { get; set; }

    /// <summary>Unix seconds of the last fetch attempt, whether it succeeded or failed.</summary>
    [JsonPropertyName("lastChecked")]
    public long LastChecked { get; set; }

    /// <summary>ETag of the last successful fetch, used for conditional requests.</summary>
    [JsonPropertyName("etag")]
    public string? Etag { get; set; }

    /// <summary>Error from the most recent attempt, or <c>null</c> if it succeeded.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
