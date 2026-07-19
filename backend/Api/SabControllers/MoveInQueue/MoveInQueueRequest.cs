using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.SabControllers.MoveInQueue;

public class MoveInQueueRequest
{
    public List<Guid> NzoIds { get; init; } = [];
    public bool MoveToTop { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public static async Task<MoveInQueueRequest> New(HttpContext httpContext)
    {
        var cancellationToken = SigtermUtil.GetCancellationToken();
        var queryIds = ParseNzoIds(httpContext);
        var bodyIds = await NzoIdsFromRequestBody(httpContext, cancellationToken).ConfigureAwait(false);
        var position = httpContext.GetRequestParam("value2");

        return new MoveInQueueRequest
        {
            NzoIds = queryIds.Concat(bodyIds).Distinct().ToList(),
            MoveToTop = IsMoveToTop(position),
            CancellationToken = cancellationToken
        };
    }

    /// <summary>
    /// SABnzbd uses absolute index 0 or the token "top" for move-to-top.
    /// Missing value2 defaults to top so a simple move call is enough for our UI.
    /// </summary>
    internal static bool IsMoveToTop(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
            return true;

        if (position.Equals("top", StringComparison.OrdinalIgnoreCase))
            return true;

        if (int.TryParse(position, out var index) && index == 0)
            return true;

        throw new BadHttpRequestException(
            "Only move-to-top is supported (value2=0 or value2=top).");
    }

    private static List<Guid> ParseNzoIds(HttpContext httpContext)
    {
        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var token in httpContext.GetQueryParamValues("value")
                     .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries |
                                                          StringSplitOptions.RemoveEmptyEntries)))
        {
            if (Guid.TryParse(token, out var id) && seen.Add(id))
                ids.Add(id);
        }

        return ids;
    }

    private static async Task<List<Guid>> NzoIdsFromRequestBody(HttpContext httpContext, CancellationToken ct)
    {
        try
        {
            await using var stream = httpContext.Request.Body;
            var deserialized = await JsonSerializer.DeserializeAsync<RequestBody>(stream, cancellationToken: ct)
                .ConfigureAwait(false);
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
