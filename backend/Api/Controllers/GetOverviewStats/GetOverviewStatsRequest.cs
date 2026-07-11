using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.Controllers.GetOverviewStats;

public class GetOverviewStatsRequest
{
    public OverviewWindow Window { get; init; } = OverviewWindow.Last24Hours;
    public CancellationToken CancellationToken { get; init; }

    public GetOverviewStatsRequest(HttpContext context)
    {
        CancellationToken = context.RequestAborted;
        var w = context.GetQueryParam("window");
        if (w is null) return;
        Window = w.ToLowerInvariant() switch
        {
            "24h" => OverviewWindow.Last24Hours,
            "7d" => OverviewWindow.Last7Days,
            "30d" => OverviewWindow.Last30Days,
            "all" => OverviewWindow.AllTime,
            _ => throw new BadHttpRequestException("Invalid window parameter (use 24h, 7d, 30d, or all)")
        };
    }

    public enum OverviewWindow
    {
        Last24Hours,
        Last7Days,
        Last30Days,
        AllTime,
    }
}
