using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.SabControllers.RemoveFromQueue;

public class RemoveFromQueueRequest()
{
    public List<Guid> NzoIds { get; init; } = [];
    public bool DeleteAll { get; init; }
    public bool DeleteFilesRequested { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public static async Task<RemoveFromQueueRequest> New(HttpContext httpContext)
    {
        var cancellationToken = SigtermUtil.GetCancellationToken();
        var query = SabDeleteValueParser.Parse(httpContext, allowFailed: false);
        var bodyIds = await NzoIdsFromRequestBody(httpContext, cancellationToken).ConfigureAwait(false);
        return new RemoveFromQueueRequest()
        {
            NzoIds = query.NzoIds.Concat(bodyIds).Distinct().ToList(),
            DeleteAll = query.DeleteAll,
            DeleteFilesRequested = httpContext.Request.Query["del_files"] == "1",
            CancellationToken = cancellationToken
        };
    }

    private static async Task<List<Guid>> NzoIdsFromRequestBody(HttpContext httpContext, CancellationToken ct)
    {
        try
        {
            await using var stream = httpContext.Request.Body;
            var deserialized = await JsonSerializer.DeserializeAsync<RequestBody>(stream, cancellationToken: ct).ConfigureAwait(false);
            return deserialized?.NzoIds ?? [];
        }
        catch
        {
            return [];
        }
    }

    private class RequestBody
    {
        [JsonPropertyName("nzo_ids")]
        public List<Guid> NzoIds { get; set; } = [];
    }
}
