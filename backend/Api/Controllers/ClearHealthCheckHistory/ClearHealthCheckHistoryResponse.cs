using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.Controllers.ClearHealthCheckHistory;

public class ClearHealthCheckHistoryResponse : BaseApiResponse
{
    [JsonPropertyName("deletedResults")]
    public required int DeletedResults { get; init; }

    [JsonPropertyName("deletedStats")]
    public required int DeletedStats { get; init; }
}
