using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Services;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.Controllers.DbRestore;

[ApiController]
[Route("api/db-restore")]
public class DbRestoreController(
    ConfigManager configManager,
    WebsocketManager websocketManager,
    DatabaseBackupStore store,
    RestartService restartService
) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        if (!HttpContext.Request.HasFormContentType)
            throw new BadHttpRequestException("Expected form body.");

        var id = HttpContext.Request.Form["id"].ToString();
        if (string.IsNullOrWhiteSpace(id))
            throw new BadHttpRequestException("Backup id is required.");

        var task = new DatabaseRestoreStageTask(
            configManager,
            websocketManager,
            store,
            restartService,
            id);

        try
        {
            var executed = await task.Execute().ConfigureAwait(false);
            if (!executed)
                return Conflict(new { error = "A database backup or restore task is already running." });
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or ArgumentException)
        {
            return BadRequest(new BaseApiResponse { Status = false, Error = ex.Message });
        }

        return Ok(new { status = true, restarting = true });
    }
}
