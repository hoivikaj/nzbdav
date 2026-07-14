using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.DbBackupDownload;

[ApiController]
[Route("api/db-backup-download")]
public class DbBackupDownloadController(DatabaseBackupStore store) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var id = HttpContext.Request.Query["id"].ToString();
        if (string.IsNullOrWhiteSpace(id))
            throw new BadHttpRequestException("Backup id is required.");

        DatabaseBackupStore.ValidateBackupId(id);
        var backupDir = store.GetBackupDirectory(id);
        if (!Directory.Exists(backupDir) || store.Get(id) is null)
            return NotFound(new BaseApiResponse { Status = false, Error = $"Backup not found: {id}" });

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"nzbdav-backup-{id}.zip\"";

        await using var archive = new ZipArchive(Response.Body, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(backupDir, file).Replace('\\', '/');
            if (relative.StartsWith(DatabaseBackupStore.RollbackFolderName + "/", StringComparison.OrdinalIgnoreCase))
                continue;

            var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await using var fileStream = System.IO.File.OpenRead(file);
            await fileStream.CopyToAsync(entryStream, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        return new EmptyResult();
    }
}
