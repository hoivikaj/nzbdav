using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Logging;

namespace NzbWebDAV.Api.Controllers.GetLogs;

[ApiController]
[Route("api/get-logs")]
public class GetLogsController(LogBufferSink sink) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        var query = HttpContext.Request.Query;

        var limit = int.TryParse(query["limit"].ToString(), out var n) ? Math.Clamp(n, 1, 5000) : 500;

        var levels = query["levels"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var source = query["source"].ToString();
        if (string.IsNullOrWhiteSpace(source)) source = null;

        var search = query["search"].ToString();
        if (string.IsNullOrWhiteSpace(search)) search = null;

        long? beforeSequence = long.TryParse(query["beforeSequence"].ToString(), out var bs) ? bs : null;

        var snapshot = sink.Snapshot(limit, levels, source, search, beforeSequence);

        return Task.FromResult<IActionResult>(Ok(new GetLogsResponse
        {
            Status = true,
            Entries = snapshot.Entries,
            CountsByLevel = snapshot.CountsByLevel,
            OldestSequence = snapshot.OldestSequence,
            NewestSequence = snapshot.NewestSequence,
            Capacity = sink.Capacity,
        }));
    }
}
