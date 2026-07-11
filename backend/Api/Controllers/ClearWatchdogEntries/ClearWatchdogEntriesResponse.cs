using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.Controllers.ClearWatchdogEntries;

public class ClearWatchdogEntriesResponse : BaseApiResponse
{
    [JsonPropertyName("deleted")]
    public required int Deleted { get; init; }
}
