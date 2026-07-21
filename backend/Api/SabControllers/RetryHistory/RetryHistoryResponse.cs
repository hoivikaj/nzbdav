using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.SabControllers.RetryHistory;

public class RetryHistoryResponse : SabBaseResponse
{
    [JsonPropertyName("nzo_id")]
    public string NzoId { get; set; } = null!;
}
