using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.SabControllers;

public sealed class SabStatusObject
{
    [JsonPropertyName("completedir")]
    public required string CompleteDir { get; init; }
}
