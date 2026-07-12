using System.Text.Json.Serialization;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

public class ArrCommand
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("commandName")]
    public string CommandName { get; set; } = null!;

    [JsonPropertyName("result")]
    public string Result { get; set; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = null!;
}