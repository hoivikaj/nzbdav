using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.SabControllers;

internal static class SabDeleteValueParser
{
    internal sealed record Result(
        List<Guid> NzoIds,
        bool DeleteAll,
        bool DeleteFailed);

    internal static Result Parse(HttpContext context, bool allowFailed)
    {
        var ids = new HashSet<Guid>();
        var deleteAll = false;
        var deleteFailed = false;

        foreach (var token in context.GetQueryParamValues("value")
                     .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries |
                                                          StringSplitOptions.RemoveEmptyEntries)))
        {
            if (token.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                deleteAll = true;
                continue;
            }

            if (allowFailed && token.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                deleteFailed = true;
                continue;
            }

            if (Guid.TryParse(token, out var id))
                ids.Add(id);
        }

        if (deleteAll)
            return new Result([], DeleteAll: true, DeleteFailed: false);
        if (deleteFailed)
            return new Result([], DeleteAll: false, DeleteFailed: true);
        return new Result(ids.ToList(), DeleteAll: false, DeleteFailed: false);
    }
}
