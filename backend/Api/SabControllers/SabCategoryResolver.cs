using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.SabControllers;

internal static class SabCategoryResolver
{
    internal static string? GetCategory(HttpContext context, ConfigManager configManager)
    {
        var category = context.GetRequestParam("cat")
                       ?? context.GetRequestParam("category");
        return category == "*"
            ? configManager.GetManualUploadCategory()
            : category;
    }
}
