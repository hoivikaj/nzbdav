using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Logging;

namespace NzbWebDAV.Api.Controllers.DownloadLogs;

[ApiController]
[Route("api/download-logs")]
public class DownloadLogsController(LogBufferSink sink) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        var query = HttpContext.Request.Query;

        var levels = query["levels"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var source = query["source"].ToString();
        if (string.IsNullOrWhiteSpace(source)) source = null;
        var search = query["search"].ToString();
        if (string.IsNullOrWhiteSpace(search)) search = null;

        var snapshot = sink.Snapshot(sink.Capacity, levels, source, search, beforeSequence: null);

        var sb = new StringBuilder(snapshot.Entries.Count * 128);
        foreach (var e in snapshot.Entries)
        {
            var ts = DateTimeOffset.FromUnixTimeMilliseconds(e.TimestampUnixMs)
                .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            sb.Append('[').Append(ts).Append("] [").Append(e.Level).Append(']');
            if (e.Source is { } src) sb.Append(" [").Append(src).Append(']');
            sb.Append(' ').AppendLine(e.Message);
            if (e.Exception is { } ex) sb.AppendLine(ex);
        }

        var fileName = $"nzbdav-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log";
        return Task.FromResult<IActionResult>(
            File(Encoding.UTF8.GetBytes(sb.ToString()), "text/plain; charset=utf-8", fileName));
    }
}
