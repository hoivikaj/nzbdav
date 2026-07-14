using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Backup;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.DbBackupUpload;

[ApiController]
[Route("api/db-backup-upload")]
[RequestSizeLimit(4L * 1024 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 4L * 1024 * 1024 * 1024)]
public class DbBackupUploadController(DatabaseBackupStore store) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        // Allow large backup uploads regardless of the global Kestrel body limit.
        var sizeFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
            sizeFeature.MaxRequestBodySize = null;

        if (!HttpContext.Request.HasFormContentType)
            throw new BadHttpRequestException("Expected multipart form body.");

        var file = HttpContext.Request.Form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            throw new BadHttpRequestException("No backup file was uploaded.");

        store.EnsureInitialized();
        var stagingPath = store.CreateStaging(DatabaseBackupKinds.Uploaded);
        try
        {
            var fileName = file.FileName ?? "upload.zip";
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractZipAsync(file, stagingPath, HttpContext.RequestAborted).ConfigureAwait(false);
            }
            else if (fileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                var target = Path.Combine(stagingPath, DatabaseBackupStore.DbSqlName);
                await using var stream = file.OpenReadStream();
                await using var output = System.IO.File.Create(target);
                await stream.CopyToAsync(output, HttpContext.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                throw new BadHttpRequestException("Upload must be a .zip backup or a .sql dump.");
            }

            ValidateSqlFiles(stagingPath);

            var notes = HttpContext.Request.Form["notes"].ToString();
            var manifest = store.CommitStaging(
                stagingPath,
                DatabaseBackupKinds.Uploaded,
                string.IsNullOrWhiteSpace(notes) ? $"Uploaded from {Path.GetFileName(fileName)}" : notes,
                preserved: false,
                appVersion: ConfigManager.AppVersion,
                lastMainMigration: null);
            stagingPath = null;

            return Ok(new { status = true, backup = manifest });
        }
        finally
        {
            store.DiscardStaging(stagingPath);
        }
    }

    private static async Task ExtractZipAsync(IFormFile file, string stagingPath, CancellationToken ct)
    {
        await using var upload = file.OpenReadStream();
        using var archive = new ZipArchive(upload, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var relative = entry.FullName.Replace('\\', '/');
            if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new BadHttpRequestException("Zip entry path is invalid.");

            var fileName = Path.GetFileName(relative);
            if (fileName is not (DatabaseBackupStore.DbSqlName
                or DatabaseBackupStore.MetricsSqlName
                or DatabaseBackupStore.WardenSqlName
                or DatabaseBackupStore.ManifestFileName))
                continue;

            var dest = Path.Combine(stagingPath, fileName);
            await using var entryStream = entry.Open();
            await using var output = System.IO.File.Create(dest);
            await entryStream.CopyToAsync(output, ct).ConfigureAwait(false);
        }
    }

    private static void ValidateSqlFiles(string stagingPath)
    {
        var sqlFiles = new[]
        {
            DatabaseBackupStore.DbSqlName,
            DatabaseBackupStore.MetricsSqlName,
            DatabaseBackupStore.WardenSqlName,
        };

        var found = false;
        foreach (var name in sqlFiles)
        {
            var path = Path.Combine(stagingPath, name);
            if (!System.IO.File.Exists(path))
                continue;
            found = true;
            using var reader = new StreamReader(path);
            var header = reader.ReadLine() ?? "";
            if (!header.Contains("PRAGMA foreign_keys", StringComparison.OrdinalIgnoreCase)
                && !header.Contains("BEGIN", StringComparison.OrdinalIgnoreCase)
                && !header.Contains("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                // Soft check — dumps may start with blank lines; read a bit more.
                var sample = header + "\n" + reader.ReadToEnd();
                if (sample.Length > 4096)
                    sample = sample[..4096];
                if (!sample.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase)
                    && !sample.Contains("BEGIN", StringComparison.OrdinalIgnoreCase))
                    throw new BadHttpRequestException($"{name} does not look like a SQLite SQL dump.");
            }
        }

        if (!found)
            throw new BadHttpRequestException("Upload did not contain any recognized .sql dump files.");
    }
}
