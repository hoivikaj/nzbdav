using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.SabControllers.GetFullStatus;

public class GetFullStatusResponse
{
    [JsonPropertyName("status")]
    public required SabStatusObject Status { get; init; }
}
