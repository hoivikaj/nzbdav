using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.SabControllers.RemoveFromHistory;

public class RemoveFromHistoryRequest
{
    public List<Guid> NzoIds { get; private init; } = [];
    public bool DeleteAll { get; private init; }
    public bool DeleteFailed { get; private init; }
    public bool DeleteFailedFilesRequested { get; private init; }
    public bool DeleteCompletedFiles { get; private init; }
    public CancellationToken CancellationToken { get; private init; }

    public static async Task<RemoveFromHistoryRequest> New(HttpContext httpContext)
    {
        var cancellationToken = SigtermUtil.GetCancellationToken();
        var query = SabDeleteValueParser.Parse(httpContext, allowFailed: true);
        var bodyIds = await NzoIdsFromRequestBody(httpContext, cancellationToken).ConfigureAwait(false);
        return new RemoveFromHistoryRequest()
        {
            NzoIds = query.NzoIds.Concat(bodyIds).Distinct().ToList(),
            DeleteAll = query.DeleteAll,
            DeleteFailed = query.DeleteFailed,
            // SAB's del_files applies to failed-job files. Failed NzbDav jobs
            // never mount WebDAV content, so accepting the flag is a no-op.
            DeleteFailedFilesRequested = httpContext.Request.Query["del_files"] == "1",
            // NzbDav's UI-specific flag intentionally controls completed mounts.
            DeleteCompletedFiles = httpContext.GetRequestParam("del_completed_files") == "1",
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
