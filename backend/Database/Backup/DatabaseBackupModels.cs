using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbWebDAV.Database.Backup;

public sealed class DatabaseBackupManifest
{
    public required string Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string Kind { get; set; }
    public string Notes { get; set; } = "";
    public bool Preserved { get; set; }
    public string? AppVersion { get; set; }
    public string? LastMainMigration { get; set; }
    public List<DatabaseBackupFileEntry> Files { get; set; } = [];
}

public sealed class DatabaseBackupFileEntry
{
    public required string Name { get; set; }
    public long Bytes { get; set; }
}

public sealed class PendingRestoreIntent
{
    public required string BackupId { get; set; }
    public required string PreRestoreBackupId { get; set; }
    public List<string> StagedFiles { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LastRestoreReport
{
    public required string BackupId { get; set; }
    public DateTimeOffset RestoredAt { get; set; }
    public long MissingBlobRefs { get; set; }
    public long CheckedRefs { get; set; }
}

public static class DatabaseBackupKinds
{
    public const string Manual = "manual";
    public const string Scheduled = "scheduled";
    public const string Uploaded = "uploaded";
    public const string PreRestore = "pre-restore";
}

public static class DatabaseBackupJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
