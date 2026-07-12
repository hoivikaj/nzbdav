using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

public class ArrDownloadClient
{
    [JsonPropertyName("enable")]
    public bool Enable { get; set; }

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("fields")]
    public List<ArrField> Fields { get; set; } = null!;

    public string? Category => (string?)Fields
        .FirstOrDefault(x => x.Name is "movieCategory" or "tvCategory")
        ?.Value;
}
