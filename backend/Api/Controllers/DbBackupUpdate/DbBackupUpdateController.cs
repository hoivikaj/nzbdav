using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.DbBackupUpdate;

[ApiController]
[Route("api/db-backup-update")]
public class DbBackupUpdateController(DatabaseBackupStore store) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        if (!HttpContext.Request.HasFormContentType)
            throw new BadHttpRequestException("Expected form body.");

        var id = HttpContext.Request.Form["id"].ToString();
        if (string.IsNullOrWhiteSpace(id))
            throw new BadHttpRequestException("Backup id is required.");

        bool? preserved = null;
        var preservedRaw = HttpContext.Request.Form["preserved"].ToString();
        if (!string.IsNullOrWhiteSpace(preservedRaw))
        {
            if (!bool.TryParse(preservedRaw, out var parsed))
                throw new BadHttpRequestException("preserved must be true or false.");
            preserved = parsed;
        }

        string? notes = null;
        if (HttpContext.Request.Form.ContainsKey("notes"))
            notes = HttpContext.Request.Form["notes"].ToString();

        var manifest = store.UpdateManifest(id, preserved, notes);
        return Task.FromResult<IActionResult>(Ok(new { status = true, backup = manifest }));
    }
}
