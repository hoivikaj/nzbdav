using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.SabControllers.GetStatus;

public class GetStatusResponse
{
    [JsonPropertyName("status")]
    public required SabStatusObject Status { get; init; }
}
