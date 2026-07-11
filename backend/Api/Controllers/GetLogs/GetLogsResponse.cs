using System.Text.Json.Serialization;
using NzbWebDAV.Logging;

namespace NzbWebDAV.Api.Controllers.GetLogs;

public class GetLogsResponse : BaseApiResponse
{
    [JsonPropertyName("entries")] public required IReadOnlyList<LogEntry> Entries { get; init; }
    [JsonPropertyName("countsByLevel")] public required IReadOnlyDictionary<string, int> CountsByLevel { get; init; }
    [JsonPropertyName("oldestSequence")] public required long OldestSequence { get; init; }
    [JsonPropertyName("newestSequence")] public required long NewestSequence { get; init; }
    [JsonPropertyName("capacity")] public required int Capacity { get; init; }
}
