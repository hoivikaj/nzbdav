using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Database.Backup;
using NzbWebDAV.Services;
using NzbWebDAV.Tasks;

namespace NzbWebDAV.Api.Controllers.DbBackupList;

[ApiController]
[Route("api/db-backup-list")]
public class DbBackupListController(DatabaseBackupStore store) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        store.EnsureInitialized();
        var response = new DbBackupListResponse
        {
            Status = true,
            Backups = store.List().ToList(),
            TaskRunning = BaseTask.IsRunning,
            PendingRestore = store.HasPendingRestore(),
            LastRestoreReport = store.ReadLastRestoreReport(),
        };
        return Task.FromResult<IActionResult>(Ok(response));
    }
}

public class DbBackupListResponse : BaseApiResponse
{
    [JsonPropertyName("backups")]
    public required List<DatabaseBackupManifest> Backups { get; init; }

    [JsonPropertyName("taskRunning")]
    public bool TaskRunning { get; init; }

    [JsonPropertyName("pendingRestore")]
    public bool PendingRestore { get; init; }

    [JsonPropertyName("lastRestoreReport")]
    public LastRestoreReport? LastRestoreReport { get; init; }
}
