using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.SabControllers.RetryHistory;

public class RetryHistoryRequest
{
    public Guid NzoId { get; private init; }
    public CancellationToken CancellationToken { get; private init; }

    public static RetryHistoryRequest New(HttpContext httpContext)
    {
        var value = httpContext.GetRequestParam("value");
        if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value, out var nzoId))
            throw new BadHttpRequestException("Missing or invalid value (nzo_id).");

        return new RetryHistoryRequest
        {
            NzoId = nzoId,
            CancellationToken = SigtermUtil.GetCancellationToken(),
        };
    }
}
