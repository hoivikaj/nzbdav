using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;

namespace NzbWebDAV.Api.Controllers.ClearHealthCheckHistory;

[ApiController]
[Route("api/clear-health-check-history")]
public class ClearHealthCheckHistoryController(DavDatabaseClient dbClient) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var ct = HttpContext.RequestAborted;

        // Delete results first so AFTER DELETE triggers can decrement stats,
        // then clear any leftover stats rows for a full reset to zero.
        var deletedResults = await dbClient.Ctx.HealthCheckResults
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        var deletedStats = await dbClient.Ctx.HealthCheckStats
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        return Ok(new ClearHealthCheckHistoryResponse
        {
            Status = true,
            DeletedResults = deletedResults,
            DeletedStats = deletedStats,
        });
    }
}
