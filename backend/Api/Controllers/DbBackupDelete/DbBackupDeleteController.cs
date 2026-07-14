using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.DbBackupDelete;

[ApiController]
[Route("api/db-backup-delete")]
public class DbBackupDeleteController(DatabaseBackupStore store) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        if (!HttpContext.Request.HasFormContentType)
            throw new BadHttpRequestException("Expected form body.");

        var id = HttpContext.Request.Form["id"].ToString();
        if (string.IsNullOrWhiteSpace(id))
            throw new BadHttpRequestException("Backup id is required.");

        store.Delete(id);
        return Task.FromResult<IActionResult>(Ok(new BaseApiResponse { Status = true }));
    }
}
