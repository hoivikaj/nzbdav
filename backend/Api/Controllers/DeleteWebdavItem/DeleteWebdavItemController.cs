using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.Controllers.DeleteWebdavItem;

[ApiController]
[Route("api/delete-webdav-item")]
public class DeleteWebdavItemController(DavDatabaseClient dbClient, ConfigManager configManager) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        if (configManager.IsEnforceReadonlyWebdavEnabled())
            return StatusCode(403, new BaseApiResponse
            {
                Status = false,
                Error = "WebDAV is read-only. Disable 'Enforce Read-Only' in Settings → WebDAV."
            });

        var path = HttpContext.Request.Form["path"].FirstOrDefault()
                   ?? throw new BadHttpRequestException("path is required");
        var ct = HttpContext.RequestAborted;

        var item = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        if (item is null) return NotFound(new BaseApiResponse { Status = false, Error = "Item not found." });
        if (item.IsProtected())
            return StatusCode(403, new BaseApiResponse { Status = false, Error = "Cannot delete protected item." });

        await DeleteRecursiveAsync(item.Id, ct).ConfigureAwait(false);
        await dbClient.Ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new BaseApiResponse { Status = true });
    }

    private async Task<DavItem?> ResolvePathAsync(string path, CancellationToken ct)
    {
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var current = DavItem.Root;
        foreach (var raw in parts)
        {
            var name = Uri.UnescapeDataString(raw);
            var child = await dbClient.GetDirectoryChildAsync(current.Id, name, ct).ConfigureAwait(false);
            if (child is null) return null;
            current = child;
        }
        return current;
    }

    private async Task DeleteRecursiveAsync(Guid id, CancellationToken ct)
    {
        var childIds = await dbClient.Ctx.Items
            .Where(x => x.ParentId == id)
            .Select(x => x.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var childId in childIds)
            await DeleteRecursiveAsync(childId, ct).ConfigureAwait(false);
        var item = await dbClient.Ctx.Items.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (item is not null) dbClient.Ctx.Items.Remove(item);
    }
}
