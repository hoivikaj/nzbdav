using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Backup;
using NzbWebDAV.Services;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.Controllers.DbBackupCreate;

[ApiController]
[Route("api/db-backup-create")]
public class DbBackupCreateController(
    ConfigManager configManager,
    WebsocketManager websocketManager,
    DatabaseBackupStore store
) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var notes = HttpContext.Request.HasFormContentType
            ? HttpContext.Request.Form["notes"].ToString()
            : "";

        var task = new DatabaseBackupTask(
            configManager,
            websocketManager,
            store,
            DatabaseBackupKinds.Manual,
            notes: string.IsNullOrWhiteSpace(notes) ? null : notes);

        var executed = await task.Execute().ConfigureAwait(false);
        if (!executed)
            return Conflict(new { error = "A database backup or restore task is already running." });

        return Ok(new BaseApiResponse { Status = true });
    }
}
