using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.Controllers.SearchIndexers;

public class SearchIndexersRequest
{
    public string Query { get; init; }
    public int Limit { get; init; }

    public SearchIndexersRequest(HttpContext context)
    {
        Query = context.GetRequestParam("q")
                ?? throw new BadHttpRequestException("Query `q` is required");

        Limit = int.TryParse(context.GetRequestParam("limit"), out var n) && n is > 0 and <= 500
            ? n
            : 100;
    }
}
