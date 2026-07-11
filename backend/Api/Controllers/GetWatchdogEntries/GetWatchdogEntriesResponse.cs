using System.Text.Json.Serialization;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Api.Controllers.GetWatchdogEntries;

public class GetWatchdogEntriesResponse : BaseApiResponse
{
    [JsonPropertyName("entries")]
    public required List<EntryDto> Entries { get; init; }

    public class EntryDto
    {
        [JsonPropertyName("clickId")] public required string ClickId { get; init; }
        [JsonPropertyName("attemptedAtUnix")] public required long AttemptedAtUnix { get; init; }
        [JsonPropertyName("contentType")] public required string ContentType { get; init; }
        [JsonPropertyName("requestedTitle")] public required string RequestedTitle { get; init; }
        [JsonPropertyName("candidateTitle")] public required string CandidateTitle { get; init; }
        [JsonPropertyName("indexerName")] public required string IndexerName { get; init; }
        [JsonPropertyName("size")] public required long Size { get; init; }
        [JsonPropertyName("rankIndex")] public required int RankIndex { get; init; }
        [JsonPropertyName("outcome")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public required WatchdogEntry.Outcome Outcome { get; init; }
        [JsonPropertyName("failReason")] public string? FailReason { get; init; }
        [JsonPropertyName("durationMs")] public required int DurationMs { get; init; }
        [JsonPropertyName("isWinner")] public required bool IsWinner { get; init; }
        [JsonPropertyName("providerHost")] public string? ProviderHost { get; init; }
        [JsonPropertyName("providerNickname")] public string? ProviderNickname { get; init; }
    }
}
