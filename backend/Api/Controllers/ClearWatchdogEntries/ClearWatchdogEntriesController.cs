using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;

namespace NzbWebDAV.Api.Controllers.ClearWatchdogEntries;

[ApiController]
[Route("api/clear-watchdog-entries")]
public class ClearWatchdogEntriesController(DavDatabaseClient dbClient) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var deleted = await dbClient.Ctx.WatchdogEntries
            .ExecuteDeleteAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return Ok(new ClearWatchdogEntriesResponse
        {
            Status = true,
            Deleted = deleted,
        });
    }
}
